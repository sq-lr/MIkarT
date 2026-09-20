from __future__ import annotations

from types import SimpleNamespace

import pytest

from app.services.filler_asset_service import (
    ClaudeFillerAssetService,
    FillerAsset,
    FillerAssetExtraction,
    MockFillerAssetService,
)
from tests.conftest import make_png


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


def test_mock_suggests_nothing_no_network():
    service = MockFillerAssetService()
    result = service.suggest(make_png(), "a beach")
    assert result == FillerAssetExtraction(filler_assets=[])


def test_claude_suggest_sends_image_and_truncates_to_max_assets():
    extraction = FillerAssetExtraction(
        filler_assets=[
            FillerAsset(keyword=f"prop_{i}", density=0.3, placement="scattered") for i in range(5)
        ]
    )
    client, messages = _stub_client(SimpleNamespace(stop_reason="end_turn", parsed_output=extraction))
    service = ClaudeFillerAssetService(max_assets=2, client=client)

    result = service.suggest(make_png(), "a busy desert canyon")

    assert [a.keyword for a in result.filler_assets] == ["prop_0", "prop_1"]
    call = messages.calls[0]
    assert call["model"] == "claude-opus-5"
    assert call["output_format"] is FillerAssetExtraction
    assert "2" in call["system"]  # max_assets is templated into the prompt
    content = call["messages"][0]["content"]
    assert content[0]["type"] == "image" and content[0]["source"]["media_type"] == "image/png"
    assert "busy desert canyon" in content[1]["text"]


def test_claude_suggest_raises_on_refusal():
    client, _ = _stub_client(SimpleNamespace(stop_reason="refusal", parsed_output=None))
    service = ClaudeFillerAssetService(max_assets=4, client=client)
    with pytest.raises(RuntimeError):
        service.suggest(make_png(), "x")


def test_filler_asset_placement_allows_all_four_values():
    # The model itself allows "landmark" -- the "never unless max_landmarks
    # says otherwise" rule is enforced by ClaudeFillerAssetService.suggest()
    # (prompt + demotion), not by the Pydantic field type. See
    # docs/decisions/0012-personalize-toggle.md.
    for placement in ("landmark", "roadside", "background", "scattered"):
        FillerAsset(keyword="x", density=0.5, placement=placement)


def test_extraction_ground_color_defaults_to_none_and_validates_hex_format():
    # None (not a hardcoded color) so a no-op/failed extraction never
    # overrides a canned theme profile's own palette[1] -- see
    # world_synthesis_service.synthesize()'s ground_color param.
    assert FillerAssetExtraction().ground_color is None
    FillerAssetExtraction(ground_color="#2f5233")  # lowercase hex is fine
    with pytest.raises(ValueError):
        FillerAssetExtraction(ground_color="not-a-color")


def test_mock_suggests_no_ground_color():
    service = MockFillerAssetService()
    result = service.suggest(make_png(), "a beach")
    assert result.ground_color is None


def test_claude_suggest_demotes_landmarks_beyond_max_landmarks():
    extraction = FillerAssetExtraction(
        filler_assets=[
            FillerAsset(keyword="lighthouse", density=0.15, placement="landmark"),
            FillerAsset(keyword="fountain", density=0.15, placement="landmark"),
            FillerAsset(keyword="clock_tower", density=0.15, placement="landmark"),
            FillerAsset(keyword="bench", density=0.5, placement="roadside"),
        ]
    )
    client, _ = _stub_client(SimpleNamespace(stop_reason="end_turn", parsed_output=extraction))
    service = ClaudeFillerAssetService(max_assets=4, client=client)

    result = service.suggest(make_png(), "a fantasy kingdom", max_landmarks=2)

    placements = {a.keyword: a.placement for a in result.filler_assets}
    assert placements["lighthouse"] == "landmark"
    assert placements["fountain"] == "landmark"
    assert placements["clock_tower"] == "scattered"  # demoted -- only 2 allowed
    assert placements["bench"] == "roadside"


def test_claude_suggest_demotes_all_landmarks_when_max_landmarks_zero():
    extraction = FillerAssetExtraction(
        filler_assets=[FillerAsset(keyword="lighthouse", density=0.15, placement="landmark")]
    )
    client, messages = _stub_client(SimpleNamespace(stop_reason="end_turn", parsed_output=extraction))
    service = ClaudeFillerAssetService(max_assets=4, client=client)

    result = service.suggest(make_png(), "a beach")  # max_landmarks defaults to 0

    assert result.filler_assets[0].placement == "scattered"
    assert "LANDMARK" not in messages.calls[0]["system"]  # the max_landmarks=0 prompt variant has no landmark step


def test_indoor_preset_hardcodes_background_filler_to_wall():
    from app.api.generate_world import INDOOR_BACKGROUND_KEYWORD, _indoor_background
    from app.services.filler_asset_service import FillerAsset

    suggested = [
        FillerAsset(keyword="mountain", density=0.5, placement="background"),
        FillerAsset(keyword="pine_treeline", density=0.8, placement="background"),
        FillerAsset(keyword="bush", density=0.4, placement="scattered"),
        FillerAsset(keyword="lamp_post", density=0.3, placement="roadside"),
    ]

    result = _indoor_background(suggested)

    backgrounds = [a for a in result if a.placement == "background"]
    assert [a.keyword for a in backgrounds] == [INDOOR_BACKGROUND_KEYWORD]
    assert backgrounds[0].density == 0.8  # densest of the replaced suggestions
    assert [a.keyword for a in result if a.placement != "background"] == ["bush", "lamp_post"]

    # No background suggested at all: the room still gets its wall.
    only_wall = _indoor_background([])
    assert [(a.keyword, a.placement, a.density) for a in only_wall] == [(INDOOR_BACKGROUND_KEYWORD, "background", 0.6)]
