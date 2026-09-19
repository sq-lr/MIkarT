from __future__ import annotations

from types import SimpleNamespace

import pytest

from app.services.text_asset_service import (
    ClaudeTextAssetService,
    ExtractedAsset,
    MockTextAssetService,
    TextAssetExtraction,
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


def test_mock_returns_empty_extraction_no_network():
    service = MockTextAssetService()
    result = service.extract("a whimsical carnival with a giant ferris wheel")
    assert result == TextAssetExtraction(key_assets=[])


def test_claude_extract_parses_structured_output_and_truncates():
    extraction = TextAssetExtraction(
        key_assets=[
            ExtractedAsset(label=f"asset_{i}", prompt=f"a fantastical thing number {i}", density=0.3)
            for i in range(5)
        ]
    )
    client, messages = _stub_client(SimpleNamespace(stop_reason="end_turn", parsed_output=extraction))
    service = ClaudeTextAssetService(max_assets=2, client=client)

    result = service.extract("a chaotic carnival with a giant whale statue")

    assert [a.label for a in result.key_assets] == ["asset_0", "asset_1"]
    call = messages.calls[0]
    assert call["model"] == "claude-opus-5"
    assert call["output_format"] is TextAssetExtraction
    assert "2" in call["system"]  # max_assets is templated into the prompt
    assert "chaotic carnival" in call["messages"][0]["content"]


def test_claude_extract_raises_on_refusal():
    client, _ = _stub_client(SimpleNamespace(stop_reason="refusal", parsed_output=None))
    service = ClaudeTextAssetService(max_assets=2, client=client)
    with pytest.raises(RuntimeError):
        service.extract("x")


def test_extracted_asset_validates_bounds():
    with pytest.raises(ValueError):
        ExtractedAsset(label="x", prompt="y", density=1.5)
    with pytest.raises(ValueError):
        ExtractedAsset(label="x", prompt="", density=0.5)
