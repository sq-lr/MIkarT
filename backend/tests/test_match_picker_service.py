from __future__ import annotations

import json
from types import SimpleNamespace

from app.services.match_picker_service import (
    ClaudeMatchPickerService,
    HeuristicMatchPickerService,
    MatchCandidate,
)


class StubMessages:
    def __init__(self, response):
        self.response = response
        self.calls: list[dict] = []

    def parse(self, **kwargs):
        self.calls.append(kwargs)
        return self.response


def _stub_client(response):
    messages = StubMessages(response)
    return SimpleNamespace(beta=SimpleNamespace(messages=messages)), messages


def _response(chosen_index, reason="because"):
    parsed = SimpleNamespace(chosen_index=chosen_index, reason=reason)
    return SimpleNamespace(stop_reason="end_turn", parsed_output=parsed)


# --- HeuristicMatchPickerService ---


def test_heuristic_picks_highest_word_overlap():
    candidates = [
        MatchCandidate(id="wrong-1", title="Bicycle"),
        MatchCandidate(id="wrong-2", title="Coat Rack"),
        MatchCandidate(id="right-1", title="Bicycle Rack"),
    ]
    picker = HeuristicMatchPickerService()
    assert picker.pick("bicycle_rack", candidates) == "right-1"


def test_heuristic_returns_none_when_nothing_overlaps():
    candidates = [MatchCandidate(id="a", title="Road Bits"), MatchCandidate(id="b", title="Ceiling Light")]
    picker = HeuristicMatchPickerService()
    assert picker.pick("street_lamp", candidates) is None


def test_heuristic_cannot_bridge_a_compound_keyword_with_no_shared_word():
    # Documents the known ceiling: no candidate shares BOTH "clock" and
    # "tower" (Poly Pizza never listed a genuine "Clock Tower"), so the
    # heuristic can only pick whichever single word happens to match --
    # here "Bell Tower" (shares "tower") wins over "Analog Clock" (shares
    # "clock") purely because it's earlier, not because it's understood to
    # be the better fit. ClaudeMatchPickerService is what actually fixes this.
    candidates = [MatchCandidate(id="tower", title="Bell Tower"), MatchCandidate(id="clock", title="Analog Clock")]
    picker = HeuristicMatchPickerService()
    assert picker.pick("clock_tower", candidates) == "tower"


# --- ClaudeMatchPickerService ---


def test_claude_picker_returns_chosen_candidate_id():
    candidates = [MatchCandidate(id="a", title="Analog Clock"), MatchCandidate(id="b", title="Bell Tower")]
    client, messages = _stub_client(_response(1, "a bell tower fits clock_tower"))
    picker = ClaudeMatchPickerService(client=client)

    result = picker.pick("clock_tower", candidates)

    assert result == "b"
    call = messages.calls[0]
    assert call["model"] == "claude-haiku-4-5-20251001"
    assert "clock_tower" in call["messages"][0]["content"]
    assert "Bell Tower" in call["messages"][0]["content"]


def test_claude_picker_returns_none_when_index_is_null():
    candidates = [MatchCandidate(id="a", title="Debris Papers")]
    client, _ = _stub_client(_response(None, "nothing fits"))
    picker = ClaudeMatchPickerService(client=client)

    assert picker.pick("trash_can", candidates) is None


def test_claude_picker_returns_none_on_out_of_range_index():
    candidates = [MatchCandidate(id="a", title="Rock")]
    client, _ = _stub_client(_response(5, "oops"))
    picker = ClaudeMatchPickerService(client=client)

    assert picker.pick("rock", candidates) is None


def test_claude_picker_returns_none_on_refusal():
    client, _ = _stub_client(SimpleNamespace(stop_reason="refusal", parsed_output=None))
    picker = ClaudeMatchPickerService(client=client)

    assert picker.pick("rock", [MatchCandidate(id="a", title="Rock")]) is None


def test_claude_picker_skips_the_call_with_no_candidates():
    client, messages = _stub_client(_response(0))
    picker = ClaudeMatchPickerService(client=client)

    assert picker.pick("rock", []) is None
    assert messages.calls == []


def test_claude_picker_accepts_a_verbose_reason():
    # Real bug seen live: structured-output generation only enforces
    # shape/type, not string length, so a Field(max_length=...) on `reason`
    # let an over-long (but otherwise valid) Claude response crash the whole
    # match with a Pydantic ValidationError instead of just logging a long
    # line. `reason` must accept arbitrary length.
    candidates = [MatchCandidate(id="a", title="Bell Tower")]
    client, _ = _stub_client(_response(0, "x" * 500))
    picker = ClaudeMatchPickerService(client=client)

    assert picker.pick("clock_tower", candidates) == "a"


def test_match_choice_model_accepts_a_reason_over_200_chars():
    from app.services.match_picker_service import _MatchChoice

    _MatchChoice.model_validate_json(json.dumps({"chosen_index": 0, "reason": "x" * 500}))
