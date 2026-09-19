from __future__ import annotations

from types import SimpleNamespace

import pytest

from app.services.vision_service import ClaudeVisionService, DetectedObject, MockVisionService, SceneUnderstanding
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


def call_system(messages: StubMessages) -> str:
    return messages.calls[0]["system"]


def test_mock_vision_is_deterministic_and_detects_objects():
    service = MockVisionService()
    a = service.analyze_image(b"same bytes", "x")
    b = service.analyze_image(b"same bytes", "y")
    assert a == b
    assert a.detected_objects


def test_claude_vision_sends_image_and_truncates_to_max_objects():
    scene = SceneUnderstanding(
        dominant_colors=["#111111"],
        brightness=0.4,
        tags=["night"],
        detected_objects=[
            DetectedObject(label=f"obj_{i}", bbox=[0.1, 0.1, 0.2, 0.2], prominence=0.5, placement="landmark" if i == 0 else "scattered")
            for i in range(6)
        ],
    )
    client, messages = _stub_client(SimpleNamespace(stop_reason="end_turn", parsed_output=scene))
    service = ClaudeVisionService(max_objects=2, client=client)

    result = service.analyze_image(make_png(), "a spooky night")

    assert [o.label for o in result.detected_objects] == ["obj_0", "obj_1"]
    assert [o.placement for o in result.detected_objects] == ["landmark", "scattered"]
    assert "placement" in call_system(messages)  # the prompt explains the field
    call = messages.calls[0]
    assert call["model"] == "claude-opus-5"
    assert call["output_format"] is SceneUnderstanding
    assert "2" in call["system"]  # max_objects is templated into the prompt
    content = call["messages"][0]["content"]
    assert content[0]["type"] == "image" and content[0]["source"]["media_type"] == "image/png"
    assert "a spooky night" in content[1]["text"]


def test_claude_vision_refusal_raises():
    client, _ = _stub_client(SimpleNamespace(stop_reason="refusal", parsed_output=None))
    service = ClaudeVisionService(max_objects=4, client=client)
    with pytest.raises(RuntimeError):
        service.analyze_image(make_png(), "x")


def test_detected_object_placement_defaults_and_is_constrained():
    assert DetectedObject(label="x", bbox=[0.1, 0.1, 0.2, 0.2], prominence=0.5).placement == "scattered"
    with pytest.raises(ValueError):
        DetectedObject(label="x", bbox=[0.1, 0.1, 0.2, 0.2], prominence=0.5, placement="hovering")
    # Structured output relies on the field being a closed enum in the JSON schema.
    schema = SceneUnderstanding.model_json_schema()
    placement_schema = schema["$defs"]["DetectedObject"]["properties"]["placement"]
    assert set(placement_schema["enum"]) == {"landmark", "roadside", "background", "scattered"}


def test_mock_profiles_cover_every_placement():
    from app.services.vision_service import _MOCK_PROFILES

    seen = {o.placement for profile in _MOCK_PROFILES for o in profile.detected_objects}
    assert seen == {"landmark", "roadside", "background", "scattered"}


def test_detected_object_bbox_validation():
    with pytest.raises(ValueError):
        DetectedObject(label="x", bbox=[1.2, 0.0, 0.1, 0.1], prominence=0.5)
    with pytest.raises(ValueError):
        DetectedObject(label="x", bbox=[0.5, 0.5, 0.0, 0.1], prominence=0.5)
    # Overhanging boxes are clipped to the unit square, not rejected.
    assert DetectedObject(label="x", bbox=[0.8, 0.8, 0.5, 0.5], prominence=0.5).bbox == [0.8, 0.8, pytest.approx(0.2), pytest.approx(0.2)]
