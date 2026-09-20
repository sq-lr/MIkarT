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


def test_filler_asset_placement_excludes_landmark():
    with pytest.raises(ValueError):
        FillerAsset(keyword="x", density=0.5, placement="landmark")
    FillerAsset(keyword="x", density=0.5, placement="roadside")  # doesn't raise
