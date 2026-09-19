"""The non-file half of the /generate-world request.

There is exactly ONE world input in this product: one image + one text
description, shared by both players. FastAPI can't bind a raw uploaded file
into a Pydantic model directly, so the image travels as a separate
`UploadFile` parameter on the route; this model represents the rest.
"""

from pydantic import BaseModel, Field


class WorldGenerationInput(BaseModel):
    description: str = Field(min_length=1, max_length=500)
