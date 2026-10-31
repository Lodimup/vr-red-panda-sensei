"""Redact credentials before sending learner or model text across boundaries."""
import os
import re

CREDENTIAL = re.compile(r"sk-[a-z0-9_-]{8,}|api[_ -]?key|password|[A-Za-z0-9+/_=-]{32,}", re.IGNORECASE)


def clean(text: str) -> str:
    key = os.getenv("SENSEI_LLM_API_KEY")
    if key:
        text = text.replace(key, "[redacted]")
    return CREDENTIAL.sub("[redacted]", text)
