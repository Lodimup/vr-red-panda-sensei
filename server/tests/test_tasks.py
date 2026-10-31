from copy import deepcopy

import pytest

from sensei_server.app import Session
from sensei_server.scene import SceneError


class Socket:
    async def send_json(self, value):
        pass


@pytest.fixture
def session(tmp_path, monkeypatch):
    monkeypatch.setenv('SENSEI_PROFILE_DIR', str(tmp_path))
    return Session(Socket(), 'learner')


def task(s, family, predicates, target=None, properties=None):
    result = s.tasks.start(family, {'english': 'Try the task.'}, [target or {'dimension': 'production', 'key': 'water'}], predicates, properties or [], None, False, s.profile['support'], s.turn_id)
    s.evidence.started(result)
    return result


def test_discover_request_attempt_and_completion(session):
    s = session
    s.scene.spawn([{'catalog_id': 'water'}], 1)
    t = task(s, 'discover_request', [{'type': 'in_region', 'object_id': 'water_1', 'region': 'learner_hand'}])
    s.turn_id = 1
    s.evidence.utterance('mizu kudasai')
    e = s.scene.move('water_1', 'learner_hand', None, 'sensei', 1, 'fulfill_request')
    completed = s.tasks.evaluate(e)
    assert completed['status'] == 'completed' and completed['completed_by'] == 'sensei'
    result = s.evidence.record({'dimension': 'production', 'key': 'water'}, 'mizu kudasai', True, 'correct', [e['event_id']])
    assert result['task_id'] == t['task_id'] and result['engine_outcome'] == 'task_completed'
    assert result['outcome'] == 'independent_success'
    assert s.summary()['practiced'][0]['independent'] == 1
    assert s.evidence.record({'dimension': 'production', 'key': 'water'}, 'mizu kudasai', True, 'correct', [])['duplicate']


def test_listen_arrange_supported_attempt_and_completion(session):
    s = session
    s.scene.spawn([{'catalog_id': 'apple'}, {'catalog_id': 'box'}], 1)
    target = {'dimension': 'listening', 'key': 'in_box'}
    task(s, 'listen_arrange', [{'type': 'contained_in', 'selector': {'catalog_id': 'apple', 'property': {'name': 'owner', 'value': 'learner'}}, 'container_id': 'box_1'}], target, [{'object_id': 'apple_1', 'name': 'owner', 'value': 'learner'}])
    s.turn_id = 1
    e = s.scene.move('apple_1', None, 'box_1', 'player', 1)
    assert s.tasks.evaluate(e)['completed_by'] == 'player'
    result = s.evidence.record(target, None, True, 'correct', [e['event_id']])
    assert result['outcome'] == 'success_after_hint'
    assert set(result['assistance']) == {'transcript_visible', 'translation_visible'}
    assert s.summary()['practiced'][0]['supported'] == 1


def test_invalid_family_and_unknown_predicate_cannot_mutate(session):
    s = session
    s.scene.spawn([{'catalog_id': 'water'}], 1)
    before = deepcopy(s.scene.events)
    for family, predicates in [('invented_family', [{'type': 'selected', 'object_id': 'water_1'}]), ('discover_request', [{'type': 'teleport', 'object_id': 'water_1'}])]:
        with pytest.raises(SceneError) as error:
            task(s, family, predicates)
        assert error.value.code == 'invalid_arguments'
        assert s.scene.events == before and s.tasks.active is None


def test_revise_abandon_and_referenced_removal(session):
    s = session
    s.scene.spawn([{'catalog_id': 'water'}, {'catalog_id': 'apple'}], 1)
    t = task(s, 'converse_roleplay', [{'type': 'selected', 'object_id': 'water_1'}])
    with pytest.raises(SceneError, match='object_in_active_task'):
        s.tasks.check_remove(['water_1'])
    revised = s.tasks.update(t['task_id'], 'revise', 'change', None, [{'type': 'selected', 'object_id': 'apple_1'}], 1)
    assert revised['task_id'] == t['task_id'] and revised['start_event_id'] == t['start_event_id']
    assert s.tasks.update(t['task_id'], 'abandon', 'done', None, None, 1)['status'] == 'abandoned'
    assert s.tasks.active is None


def test_assistance_before_utterance_is_not_independent(session):
    s = session
    s.scene.spawn([{'catalog_id': 'water'}], 0)
    task(s, 'discover_request', [{'type': 'in_region', 'object_id': 'water_1', 'region': 'learner_hand'}])
    s.evidence.assistance('model_answer')
    s.turn_id = 1
    s.evidence.utterance('mizu')
    result = s.evidence.record({'dimension': 'production', 'key': 'water'}, 'mizu', True, 'correct', [])
    assert result['outcome'] == 'success_after_model'
    assert result['assistance'] == ['model_answer']


def test_player_action_emits_completed_task_and_scene(session):
    import asyncio

    class CollectingSocket:
        def __init__(self):
            self.messages = []
        async def send_json(self, message):
            self.messages.append(message)

    s = session
    s.ws = CollectingSocket()
    s.scene.spawn([{'catalog_id': 'apple'}], 0)
    task(s, 'listen_arrange', [{'type': 'in_region', 'object_id': 'apple_1', 'region': 'right'}])
    asyncio.run(s.action({'type': 'player_move', 'instance_id': 'apple_1', 'region': 'right'}))
    assert [m['type'] for m in s.ws.messages] == ['task', 'scene', 'action_result']
    assert s.ws.messages[0]['task']['status'] == 'completed'
    assert s.ws.messages[1]['snapshot']['active_task'] is None
    assert s.ws.messages[2]['ok'] is True


def test_evidence_level_rule_requires_fresh_independent_attempts(session):
    s = session
    for turn in range(1, 9):
        s.turn_id = turn
        s.evidence.utterance('mizu')
        s.evidence.record({'dimension': 'production', 'key': 'water'}, 'mizu', True, 'correct', [], fresh=turn <= 2)
    assert s.profile['working_level'] == 'n5'
    assert s.profile['level_history'][-1]['reason'] == 'evidence_up'
    assert len(s.profile['level_history'][-1]['evidence_ids']) == 8


def test_hidden_level_in_task_instruction_is_rejected(session):
    s = session
    s.scene.spawn([{'catalog_id': 'apple'}], 0)
    before = deepcopy(s.scene.events)
    with pytest.raises(SceneError, match='invalid_arguments'):
        s.tasks.start('listen_arrange', {'english': 'Your level is N5.'}, [{'dimension': 'listening', 'key': 'apple'}], [{'type': 'selected', 'object_id': 'apple_1'}], [], None, False, s.profile['support'], 1)
    assert s.scene.events == before
