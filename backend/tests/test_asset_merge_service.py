from __future__ import annotations

from types import SimpleNamespace

from app.services.asset_merge_service import (
    ClaudeAssetMergeService,
    MergeCandidate,
    MergeDecision,
    MockAssetMergeService,
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


def _response(decisions):
    parsed = SimpleNamespace(decisions=[SimpleNamespace(**d) for d in decisions])
    return SimpleNamespace(stop_reason="end_turn", parsed_output=parsed)


# --- MockAssetMergeService ---


def test_mock_keeps_every_candidate_with_its_own_placement():
    candidates = [
        MergeCandidate(index=0, source="photo", label="mountain", placement="background", density=0.2),
        MergeCandidate(index=1, source="text", label="dragon_statue", placement="landmark", density=0.1),
    ]
    result = MockAssetMergeService().merge(candidates, max_assets=12)

    assert result.decisions == [
        MergeDecision(index=0, keep=True, placement="background"),
        MergeDecision(index=1, keep=True, placement="landmark"),
    ]


# --- ClaudeAssetMergeService ---


def test_claude_merge_applies_returned_decisions():
    candidates = [
        MergeCandidate(index=0, source="photo", label="mountain", placement="background", density=0.2),
        MergeCandidate(index=1, source="filler", label="cliff", placement="background", density=0.2),
        MergeCandidate(index=2, source="text", label="dragon_statue", placement="landmark", density=0.1),
    ]
    client, messages = _stub_client(
        _response(
            [
                dict(index=0, keep=True, placement="background"),
                dict(index=1, keep=False, placement="scattered"),
                dict(index=2, keep=True, placement="landmark"),
            ]
        )
    )
    service = ClaudeAssetMergeService(client=client)

    result = service.merge(candidates, max_assets=12)

    assert result.decisions == [
        MergeDecision(index=0, keep=True, placement="background"),
        MergeDecision(index=2, keep=True, placement="landmark"),
    ]
    call = messages.calls[0]
    assert call["model"] == "claude-haiku-4-5-20251001"
    assert "mountain" in call["messages"][0]["content"]
    assert "12" in call["system"]


def test_claude_merge_demotes_a_second_background_even_if_claude_keeps_both():
    # Defense in depth: the prompt asks for exactly one "background", but a
    # structured-output call is never trusted alone for a hard invariant a
    # creative response could violate.
    candidates = [
        MergeCandidate(index=0, source="photo", label="mountain", placement="background", density=0.2),
        MergeCandidate(index=1, source="filler", label="cliff", placement="background", density=0.2),
    ]
    client, _ = _stub_client(
        _response([dict(index=0, keep=True, placement="background"), dict(index=1, keep=True, placement="background")])
    )
    service = ClaudeAssetMergeService(client=client)

    result = service.merge(candidates, max_assets=12)

    placements = {d.index: d.placement for d in result.decisions}
    assert placements[0] == "background"
    assert placements[1] == "scattered"


def test_claude_merge_demotes_a_third_landmark_even_if_claude_keeps_all():
    candidates = [
        MergeCandidate(index=0, source="photo", label="a", placement="landmark", density=0.1),
        MergeCandidate(index=1, source="photo", label="b", placement="landmark", density=0.1),
        MergeCandidate(index=2, source="text", label="c", placement="landmark", density=0.1),
    ]
    client, _ = _stub_client(
        _response(
            [
                dict(index=0, keep=True, placement="landmark"),
                dict(index=1, keep=True, placement="landmark"),
                dict(index=2, keep=True, placement="landmark"),
            ]
        )
    )
    service = ClaudeAssetMergeService(client=client)

    result = service.merge(candidates, max_assets=12)

    placements = {d.index: d.placement for d in result.decisions}
    assert placements[0] == "landmark"
    assert placements[1] == "landmark"
    assert placements[2] == "scattered"


def test_claude_merge_defaults_a_missing_candidate_to_kept_unchanged():
    candidates = [MergeCandidate(index=0, source="photo", label="rock", placement="scattered", density=0.3)]
    client, _ = _stub_client(_response([]))  # Claude returned no decision for index 0 at all
    service = ClaudeAssetMergeService(client=client)

    result = service.merge(candidates, max_assets=12)

    assert result.decisions == [MergeDecision(index=0, keep=True, placement="scattered")]


def test_claude_merge_raises_on_refusal():
    client, _ = _stub_client(SimpleNamespace(stop_reason="refusal", parsed_output=None))
    service = ClaudeAssetMergeService(client=client)
    candidates = [MergeCandidate(index=0, source="photo", label="rock", placement="scattered", density=0.3)]

    try:
        service.merge(candidates, max_assets=12)
        assert False, "expected RuntimeError"
    except RuntimeError:
        pass


def test_claude_merge_skips_the_call_with_no_candidates():
    client, messages = _stub_client(_response([]))
    service = ClaudeAssetMergeService(client=client)

    result = service.merge([], max_assets=12)

    assert result.decisions == []
    assert messages.calls == []
