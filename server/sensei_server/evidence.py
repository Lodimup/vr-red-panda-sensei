"""Record model assessments with engine-owned evidence and assistance rules."""
from __future__ import annotations

from copy import deepcopy
import re
import unicodedata

from .profile import now
from .sanitize import clean
from .scene import SceneError
from .tasks import DIMENSIONS

AIDS = {
    'listening': {'transcript_visible', 'translation_visible', 'highlight_answer', 'demonstration', 'repetition'},
    'vocabulary': {'model_answer', 'partial_phrase', 'vocabulary_card', 'audio_model', 'demonstration'},
    'production': {'model_answer', 'partial_phrase', 'audio_model', 'demonstration'},
    'reading': {'translation_visible', 'translation_hint', 'audio_model', 'highlight_answer'},
    'interaction': {'model_answer', 'demonstration'}, 'register': {'model_answer', 'demonstration'},
}
PROHIBITED = re.compile(r'pronunc|accent|intonation|pitch|発音|アクセント|イントネーション|ピッチ', re.I)


def normalized(value):
    return ' '.join(unicodedata.normalize('NFKC', value).lower().split())


class EvidenceBook:
    def __init__(self, session):
        self.session = session
        self.sequence = 0
        self.utterances = []
        self.aids = []
        self.task_starts = {}
        self.event_positions = {}

    def tick(self):
        self.sequence += 1
        return self.sequence

    def utterance(self, text):
        self.utterances.append({'turn': self.session.turn_id, 'text': text, 'sequence': self.tick(), 'support': deepcopy(self.session.profile['support']), 'id': f'l{len(self.utterances)+1}'})

    def scene_event(self, event):
        self.event_positions[event["event_id"]] = {"sequence": self.tick(), "support": deepcopy(self.session.profile["support"])}

    def started(self, task):
        self.task_starts[task['task_id']] = self.tick()

    def assistance(self, kind, target_key=None):
        task = self.session.tasks.active
        if not task and not target_key:
            return
        targets = [t for t in task['skill_targets'] if target_key is None or t['key'] == target_key] if task else []
        if not targets and target_key:
            targets = [{'key': target_key, 'dimension': None}]
        seq = self.tick()
        self.aids.extend({'kind': kind, 'key': t['key'], 'dimension': t['dimension'], 'sequence': seq, 'turn': self.session.turn_id} for t in targets)

    def record(self, target, excerpt, success, assessment, event_ids, task_id=None, fresh=False, uncertainty='low'):
        s = self.session
        if not isinstance(target, dict) or set(target) - {'dimension', 'key', 'japanese'} or (not isinstance(target.get('dimension'), str) or target['dimension'] not in DIMENSIONS) or not isinstance(target.get('key'), str) or not 1 <= len(target['key']) <= 40 or ('japanese' in target and (not isinstance(target['japanese'], str) or len(target['japanese']) > 30)) or type(success) is not bool or assessment not in ('correct', 'minor_issue', 'meaning_blocking_error', 'unclear_input') or type(fresh) is not bool or uncertainty not in ('low', 'high') or not isinstance(event_ids, list) or any(type(e) is not int for e in event_ids):
            raise SceneError('invalid_arguments')
        if any(PROHIBITED.search(v) for v in target.values() if isinstance(v, str)) or (excerpt is not None and (not isinstance(excerpt, str) or len(excerpt) > 120 or PROHIBITED.search(excerpt))):
            raise SceneError('invalid_arguments')
        if any(clean(v) != v for v in [target['key'], target.get('japanese', ''), excerpt or '']):
            raise SceneError('looks_like_credential')
        event_ids = list(dict.fromkeys(event_ids))
        for item in s.profile['evidence']:
            if item.get('session_id') == s.session_id and item.get('turn_id') == s.turn_id and not item.get('superseded') and item['target'].get('dimension') == target['dimension'] and item['target'].get('key') == target['key']:
                return {**deepcopy(item), 'duplicate': True}
        task = None
        for t in reversed(s.tasks.history):
            eligible = t['status'] == 'active' or (t['status'] in ('completed', 'ended', 'abandoned') and t.get('_closed_turn', -10) >= s.turn_id - 1)
            if eligible and ((task_id is not None and t['task_id'] == task_id) or (task_id is None and t['status'] != 'abandoned')):
                task = t
                break
        if task_id is not None and task is None:
            raise SceneError('no_active_task')
        events = []
        for ident in event_ids:
            e = next((e for e in s.scene.events if e['event_id'] == ident), None)
            if e is None or (task and e['event_id'] <= task['start_event_id']):
                raise SceneError('unknown_event')
            events.append(e)
        if target['dimension'] == 'listening' and (not events or any(e['actor'] != 'player' for e in events)):
            raise SceneError('invalid_arguments')
        source = None
        if excerpt:
            source = next((u for u in reversed(self.utterances) if u['turn'] >= s.turn_id - 1 and normalized(excerpt) in normalized(u['text'])), None)
            if source is None:
                raise SceneError('excerpt_not_in_transcript')
        elif target['dimension'] != 'listening':
            raise SceneError('invalid_arguments')
        event_source = self.event_positions.get(event_ids[0]) if event_ids else None
        source_seq = source['sequence'] if source else (event_source['sequence'] if event_source else self.sequence + 1)
        prior = [e for e in s.profile['evidence'] if not e.get('superseded') and e.get('session_id') == s.session_id and e['target'].get('dimension') == target['dimension'] and e['target'].get('key') == target['key']]
        boundary = max(self.task_starts.get(task['task_id'], 0) if task else 0, prior[-1].get('_source_sequence', 0) if prior else 0)
        relevant = AIDS[target['dimension']]
        previous_utterance_seq = max((u['sequence'] for u in self.utterances if u['sequence'] < source_seq), default=0)
        aids = {a['kind'] for a in self.aids if a['key'] == target['key'] and a['dimension'] in (None, target['dimension']) and boundary < a['sequence'] < source_seq and a['kind'] in relevant and (a['kind'] != 'audio_model' or a['sequence'] > previous_utterance_seq)}
        support = source['support'] if source else (event_source['support'] if event_source else s.profile['support'])
        if target['dimension'] == 'listening' and support['show_japanese']:
            aids.add('transcript_visible')
        if target['dimension'] in ('listening', 'reading') and support['show_translation']:
            aids.add('translation_visible')
        outcome = ('unresolved' if assessment == 'unclear_input' else 'confirmed_incorrect' if not success else 'completed_by_demonstration' if 'demonstration' in aids else 'success_after_model' if aids & {'model_answer', 'audio_model'} else 'success_after_hint' if aids - {'repetition'} else 'success_after_repetition' if 'repetition' in aids else 'independent_success')
        record = {'attempt_id': f"a{len(s.profile['evidence'])+1}", 'session_id': s.session_id, 'turn_id': s.turn_id, 'target': deepcopy(target), 'transcript_excerpt': excerpt, 'source_utterance_id': source['id'] if source else None, 'event_ids': event_ids[:], 'actors': [e['actor'] for e in events], 'engine_outcome': ('no_task' if task is None else 'task_active' if task['status'] == 'active' else 'task_completed' if task['status'] == 'completed' else 'task_ended'), 'task_id': task['task_id'] if task else None, 'language_assessment': assessment, 'assessment_source': 'model', 'communicative_success': success, 'assistance': sorted(aids), 'outcome': outcome, 'is_fresh_variation': fresh, 'uncertainty': 'high' if outcome == 'unresolved' else uncertainty, 'rule_version': 'rules-v1', 'superseded': False, 'at': now(), '_source_sequence': source_seq}
        s.profile['evidence'].append(record)
        apply_level_rules(s.profile)
        s.save()
        self.tick()
        return deepcopy(record)



def apply_level_rules(profile):
    """Apply S51 against resolved evidence after the last effective level change."""
    levels = ('pre_n5', 'n5', 'n4', 'n3', 'n2', 'n1')
    history = [h for h in profile['level_history'] if not h.get('superseded')]
    start = history[-1].get('attempt_count', 0) if history else 0
    window = [e for e in profile['evidence'][start:] if not e.get('superseded') and e['outcome'] != 'unresolved']
    index = levels.index(profile['working_level'])
    independent = [e for e in window if e['outcome'] == 'independent_success']
    reason = None
    used = window
    if index < len(levels)-1 and len(window) >= 8 and len(independent) >= 6 and sum(e.get('is_fresh_variation', False) for e in independent) >= 2:
        new_index, reason = index + 1, 'evidence_up'
    elif index > 0 and len(window) >= 6:
        used = window[-6:]
        wrong = [e for e in used if e['outcome'] == 'confirmed_incorrect']
        if len(wrong) >= 3 and len({(e['target']['dimension'], e['target']['key']) for e in wrong}) >= 2:
            new_index, reason = index - 1, 'evidence_down'
    if reason:
        profile['working_level'] = levels[new_index]
        profile['level_history'].append({'from': levels[index], 'to': levels[new_index], 'reason': reason, 'at': now(), 'attempt_count': len(profile['evidence']), 'evidence_ids': [e['attempt_id'] for e in used]})
