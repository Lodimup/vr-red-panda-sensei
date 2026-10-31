"""Engine-owned task validation and scene predicate evaluation."""
from __future__ import annotations

from copy import deepcopy
import re
import unicodedata

from .scene import CATALOG, PRACTICE, REGIONS, SceneError
from .sanitize import clean

FAMILIES = {'discover_request', 'listen_arrange', 'converse_roleplay'}
DIMENSIONS = {'vocabulary', 'listening', 'production', 'reading', 'interaction', 'register'}
FIELDS = {
    'selected': ({'object_id'}, {'object_id'}),
    'unchanged': ({'object_id'}, {'object_id'}),
    'in_region': ({'object_id', 'selector', 'region'}, {'region'}),
    'contained_in': ({'object_id', 'selector', 'container_id'}, {'container_id'}),
    'quantity': ({'selector', 'region', 'container_id', 'count', 'comparison'}, {'selector', 'count'}),
    'left_of': ({'object_id', 'reference_id'}, {'object_id', 'reference_id'}),
    'right_of': ({'object_id', 'reference_id'}, {'object_id', 'reference_id'}),
    'before': ({'object_id', 'reference_id'}, {'object_id', 'reference_id'}),
}


def fail(code='invalid_arguments'):
    raise SceneError(code)


def check_presentation(value):
    text = ' '.join(unicodedata.normalize('NFKC', value).lower().split())
    patterns = (r'working[ _]level', r'pre_n[1-5]', r'your (?:jlpt |japanese )?level(?: is)?',
                r"\b(?:you(?:'re| are)|ur|u r)\s+(?:at\s+)?(?:the\s+)?(?:jlpt\s+)?(?:level\s+)?(?:pre[- ]?)?n[- ]?(?:[1-5]|one|two|three|four|five)\b",
                r'\byour\s+(?:jlpt\s+)?(?:japanese\s+)?(?:ability|level)\s+(?:is|=)\s+(?:jlpt\s+)?(?:pre[- ]?)?n[- ]?(?:[1-5]|one|two|three|four|five)\b',
                r'あなたのレベル', r'(?:あなた|きみ|君)(?:は|の日本語は)\s*(?:jlpt\s*)?n[1-5]')
    if any(re.search(pattern, text) for pattern in patterns):
        fail()


def matches(obj, selector, properties):
    prop = selector.get('property')
    return obj['catalog_id'] == selector['catalog_id'] and (not prop or any(
        x['object_id'] == obj['id'] and x['name'] == prop['name'] and x['value'] == prop['value'] for x in properties))


def predicate_true(p, task, scene):
    kind = p['type']
    obj = scene.find(p.get('object_id'))
    ref = scene.find(p.get('reference_id'))
    subjects = [obj] if obj else ([o for o in scene.instances if matches(o, p['selector'], task['scenario_properties'])] if p.get('selector') else [])
    if kind == 'selected':
        return scene.selected_id == p['object_id']
    if kind in ('in_region', 'contained_in', 'quantity'):
        placed = [o for o in subjects if (o['container_id'] == p['container_id'] if p.get('container_id') else o['container_id'] is None and o['region'] == p.get('region'))]
        if kind != 'quantity':
            return bool(placed)
        count = len(placed)
        return {'eq': count == p['count'], 'gte': count >= p['count'], 'lte': count <= p['count']}[p.get('comparison') or 'eq']
    if kind in ('left_of', 'right_of'):
        if not obj or not ref or obj['container_id'] or ref['container_id'] or obj['region'] not in PRACTICE or ref['region'] not in PRACTICE:
            return False
        position = lambda o: (('left', 'center', 'right').index(o['region']), o['slot'])
        return position(obj) < position(ref) if kind == 'left_of' else position(obj) > position(ref)
    events = [e for e in scene.events if e['event_id'] > task['predicates_since_event_id']]
    if kind == 'unchanged':
        return bool(obj) and not any(e['kind'] in ('moved', 'removed') and p['object_id'] in e['instance_ids'] for e in events)
    if kind == 'before':
        first = lambda ident: next((e['event_id'] for e in events if e['kind'] == 'moved' and ident in e['instance_ids']), None)
        a, b = first(p['object_id']), first(p['reference_id'])
        return a is not None and b is not None and a < b
    return False


def validate(scene, family, instruction, targets, predicates, properties, role=None):
    if not isinstance(family, str) or family not in FAMILIES or not isinstance(instruction, dict) or set(instruction) - {'japanese', 'english'} or not instruction:
        fail()
    for key, value in instruction.items():
        if not isinstance(value, str) or not 1 <= len(value) <= (80 if key == 'japanese' else 200) or clean(value) != value:
            fail()
    if role is not None and (not isinstance(role, str) or len(role) > 40 or clean(role) != role):
        fail()
    for value in instruction.values():
        check_presentation(value)
    if role is not None:
        check_presentation(role)
    if not isinstance(targets, list) or not 1 <= len(targets) <= 3:
        fail()
    for target in targets:
        if not isinstance(target, dict) or set(target) - {'dimension', 'key', 'japanese'} or (not isinstance(target.get('dimension'), str) or target['dimension'] not in DIMENSIONS) or not isinstance(target.get('key'), str) or not 1 <= len(target['key']) <= 40:
            fail()
        if 'japanese' in target and (not isinstance(target['japanese'], str) or len(target['japanese']) > 30 or clean(target['japanese']) != target['japanese']):
            fail()
        if target.get('japanese'):
            check_presentation(target['japanese'])
    if not isinstance(properties, list) or len(properties) > 4:
        fail()
    for prop in properties:
        if not isinstance(prop, dict) or set(prop) != {'object_id', 'name', 'value'} or not isinstance(prop['object_id'], str) or not isinstance(prop['name'], str) or not re.fullmatch('[a-z_]{1,24}', prop['name']) or not isinstance(prop['value'], str) or not 1 <= len(prop['value']) <= 24 or clean(prop['value']) != prop['value']:
            fail()
        check_presentation(prop['value'])
        if not scene.find(prop['object_id']):
            fail('impossible_task')
    if not isinstance(predicates, list) or not 1 <= len(predicates) <= 3:
        fail()
    for p in predicates:
        if not isinstance(p, dict) or not isinstance(p.get('type'), str) or p['type'] not in FIELDS:
            fail()
        kind = p['type']; allowed, required = FIELDS[kind]
        if set(p) - (allowed | {'type'}) or not required <= p.keys():
            fail()
        if kind in ('in_region', 'contained_in') and (('object_id' in p) == ('selector' in p)):
            fail()
        if kind == 'quantity' and (('region' in p) == ('container_id' in p)):
            fail()
        if 'region' in p and (not isinstance(p['region'], str) or p['region'] not in REGIONS):
            fail()
        if kind == 'quantity' and (type(p['count']) is not int or not 1 <= p['count'] <= 8 or p.get('comparison', 'eq') not in ('eq', 'gte', 'lte')):
            fail()
        for key in ('object_id', 'reference_id', 'container_id'):
            if key in p and (not isinstance(p[key], str) or not scene.find(p[key])):
                fail('impossible_task')
        if p.get('object_id') and p.get('object_id') == p.get('reference_id'):
            fail('impossible_task')
        if p.get('container_id') and not CATALOG[scene.find(p['container_id'])['catalog_id']].container:
            fail('impossible_task')
        selector = p.get('selector')
        if selector is not None:
            if not isinstance(selector, dict) or set(selector) - {'catalog_id', 'property'} or not isinstance(selector.get('catalog_id'), str):
                fail()
            if selector['catalog_id'] not in CATALOG:
                fail('impossible_task')
            prop = selector.get('property')
            if prop is not None:
                if not isinstance(prop, dict) or set(prop) != {'name', 'value'} or not all(isinstance(v, str) for v in prop.values()):
                    fail()
                if not any(x['name'] == prop['name'] and x['value'] == prop['value'] for x in properties):
                    fail('impossible_task')
            if kind == 'quantity' and p.get('comparison', 'eq') != 'lte' and sum(matches(o, selector, properties) for o in scene.instances) < p['count']:
                fail('impossible_task')
        if kind == 'contained_in' and ((p.get('object_id') and CATALOG[scene.find(p['object_id'])['catalog_id']].container) or (selector and CATALOG[selector['catalog_id']].container)):
            fail('impossible_task')
        if kind in ('left_of', 'right_of') and any(scene.find(p[k])['container_id'] for k in ('object_id', 'reference_id')):
            fail('impossible_task')
    for a in predicates:
        for b in predicates:
            if a is b:
                continue
            if a['type'] in ('in_region', 'contained_in') and b['type'] in ('in_region', 'contained_in') and a.get('object_id') and a.get('object_id') == b.get('object_id') and (a.get('region'), a.get('container_id')) != (b.get('region'), b.get('container_id')):
                fail('impossible_task')
            if a['type'] in ('left_of', 'right_of', 'before') and b['type'] in ('left_of', 'right_of', 'before'):
                opposite = (a['type'] == b['type'] and a['object_id'] == b['reference_id'] and a['reference_id'] == b['object_id']) or ({a['type'], b['type']} == {'left_of', 'right_of'} and a['object_id'] == b['object_id'] and a['reference_id'] == b['reference_id'])
                if opposite:
                    fail('impossible_task')
    probe = {'scenario_properties': properties, 'predicates_since_event_id': len(scene.events)}
    if all(p['type'] == 'unchanged' for p in predicates) or all(predicate_true(p, probe, scene) for p in predicates):
        fail('impossible_task')


class TaskBook:
    def __init__(self, scene):
        self.scene = scene
        self.history = []
        self.counter = 0

    @property
    def active(self):
        return self.history[-1] if self.history and self.history[-1]['status'] == 'active' else None

    def start(self, family, instruction, targets, predicates, properties, role, fresh, support, turn):
        if self.active:
            fail('task_already_active')
        if type(fresh) is not bool:
            fail()
        validate(self.scene, family, instruction, targets, predicates, properties, role)
        self.counter += 1
        event = self.scene._event('task_started', 'sensei', [], turn, mutates=False)
        task = dict(task_id=f't{self.counter}', status='active', family=family, instruction=deepcopy(instruction), skill_targets=deepcopy(targets), predicates=deepcopy(predicates), scenario_properties=deepcopy(properties), role=role, is_fresh_variation=fresh, source_revision=self.scene.revision, start_event_id=event['event_id'], predicates_since_event_id=event['event_id'], assistance_baseline=deepcopy(support))
        self.history.append(task)
        return deepcopy(task)

    def update(self, task_id, action, reason, instruction, predicates, turn):
        task = self.active
        if not task or task['task_id'] != task_id:
            fail('no_active_task')
        if action not in ('revise', 'abandon') or not isinstance(reason, str) or not 1 <= len(reason) <= 120:
            fail()
        if action == 'revise':
            if instruction is None and predicates is None:
                fail()
            if predicates is not None:
                validate(self.scene, task['family'], instruction if instruction is not None else task['instruction'], task['skill_targets'], predicates, task['scenario_properties'], task['role'])
            else:
                # Instruction-only revisions do not require the current predicates to be unsatisfied.
                validate_instruction = instruction
                if not isinstance(validate_instruction, dict) or not validate_instruction or set(validate_instruction) - {'japanese', 'english'} or any(not isinstance(v, str) or not 1 <= len(v) <= (80 if k == 'japanese' else 200) or clean(v) != v for k, v in validate_instruction.items()):
                    fail()
                for value in validate_instruction.values():
                    check_presentation(value)
            event = self.scene._event('task_revised', 'sensei', [], turn, mutates=False)
            if instruction is not None:
                task['instruction'] = deepcopy(instruction)
            if predicates is not None:
                task['predicates'] = deepcopy(predicates)
                task['predicates_since_event_id'] = event['event_id']
                task['source_revision'] = self.scene.revision
        else:
            task['status'] = 'abandoned'
            task['_closed_turn'] = turn
            self.scene._event('task_abandoned', 'sensei', [], turn, mutates=False)
        return self.view(task)

    def end(self, turn):
        task = self.active
        if task is None:
            return None
        task['status'] = 'ended'
        task['_closed_turn'] = turn
        self.scene._event('task_ended', 'sensei', [], turn, mutates=False)
        return self.view(task)

    @staticmethod
    def view(task):
        return deepcopy({k: v for k, v in task.items() if not k.startswith('_')})

    def evaluate(self, event):
        task = self.active
        if task and all(predicate_true(p, task, self.scene) for p in task['predicates']):
            task.update(status='completed', completed_by=event['actor'], completed_event_id=event['event_id'], _closed_turn=event['turn_id'])
            self.scene._event('task_completed', event['actor'], [], event['turn_id'], mutates=False)
            return self.view(task)
        return None

    def check_remove(self, ids):
        task = self.active
        if not task:
            return
        for p in task['predicates']:
            if any(p.get(k) in ids for k in ('object_id', 'reference_id', 'container_id')):
                fail('object_in_active_task')
            if p.get('selector'):
                remaining = sum(matches(o, p['selector'], task['scenario_properties']) for o in self.scene.instances if o['id'] not in ids)
                needed = p['count'] if p['type'] == 'quantity' and p.get('comparison', 'eq') != 'lte' else (1 if p['type'] in ('in_region', 'contained_in') else 0)
                if remaining < needed:
                    fail('object_in_active_task')
        if any(p['object_id'] in ids for p in task['scenario_properties']):
            fail('object_in_active_task')
