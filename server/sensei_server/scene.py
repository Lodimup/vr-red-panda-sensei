"""Authoritative object placement and scene events."""
from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class CatalogEntry:
    japanese: str
    reading: str
    romaji: str
    english: str
    container: bool = False


CATALOG = {
    "water": CatalogEntry("水", "みず", "mizu", "water"),
    "tea": CatalogEntry("お茶", "おちゃ", "ocha", "tea"),
    "cup": CatalogEntry("コップ", "コップ", "koppu", "cup"),
    "apple": CatalogEntry("りんご", "りんご", "ringo", "apple"),
    "banana": CatalogEntry("バナナ", "バナナ", "banana", "banana"),
    "rice_ball": CatalogEntry("おにぎり", "おにぎり", "onigiri", "rice ball"),
    "book": CatalogEntry("本", "ほん", "hon", "book"),
    "pen": CatalogEntry("ペン", "ペン", "pen", "pen"),
    "bag": CatalogEntry("かばん", "かばん", "kaban", "bag", True),
    "box": CatalogEntry("箱", "はこ", "hako", "box", True),
    "key": CatalogEntry("鍵", "かぎ", "kagi", "key"),
    "umbrella": CatalogEntry("傘", "かさ", "kasa", "umbrella"),
}
REGIONS = frozenset(("left", "center", "right", "learner_hand", "sensei_side"))
PRACTICE = frozenset(("left", "center", "right"))


class SceneError(ValueError):
    def __init__(self, code: str, detail: str = "") -> None:
        super().__init__(detail or code)
        self.code = code


class Scene:
    def __init__(self) -> None:
        self.revision = 0
        self.selected_id: str | None = None
        self.instances: list[dict[str, Any]] = []
        self.events: list[dict[str, Any]] = []
        self._ids: dict[str, int] = {}
        self._slots: dict[str, int] = {}

    def _slot(self, region: str) -> int:
        if region not in PRACTICE:
            return 0
        self._slots[region] = self._slots.get(region, 0) + 1
        return self._slots[region]

    def _event(self, kind: str, actor: str, ids: list[str], turn_id: int,
               origin: str | None = None, destination: str | None = None,
               purpose: str | None = None, *, mutates: bool = True) -> dict[str, Any]:
        if mutates:
            self.revision += 1
        event = dict(event_id=len(self.events) + 1, revision=self.revision, actor=actor,
                     kind=kind, instance_ids=list(ids), **{"from": origin, "to": destination},
                     purpose=purpose, turn_id=turn_id)
        self.events.append(event)
        return deepcopy(event)

    def find(self, instance_id: str) -> dict[str, Any] | None:
        return next((obj for obj in self.instances if obj["id"] == instance_id), None)

    def snapshot(self, support: dict[str, Any], task: dict[str, Any] | None) -> dict[str, Any]:
        return deepcopy(dict(revision=self.revision, selected_id=self.selected_id,
                             instances=self.instances, active_task=task, support=support))

    def spawn(self, objects: list[dict[str, Any]], turn_id: int) -> dict[str, Any]:
        if not isinstance(objects, list) or not 1 <= len(objects) <= 6:
            raise SceneError("invalid_arguments", "objects")
        expanded = []
        for spec in objects:
            if not isinstance(spec, dict) or set(spec) - {"catalog_id", "region", "quantity"}:
                raise SceneError("invalid_arguments", "objects")
            catalog = spec.get("catalog_id")
            region = spec.get("region") or "center"
            quantity = spec.get("quantity") if spec.get("quantity") is not None else 1
            if not isinstance(catalog, str):
                raise SceneError("invalid_arguments", "catalog_id")
            if catalog not in CATALOG:
                raise SceneError("unknown_catalog_id")
            if not isinstance(region, str) or region not in REGIONS - {"learner_hand"} or type(quantity) is not int or not 1 <= quantity <= 4:
                raise SceneError("invalid_arguments", "region or quantity")
            expanded.extend([(catalog, region)] * quantity)
        if len(expanded) > 6:
            raise SceneError("invalid_arguments", "quantity")
        if len(self.instances) + len(expanded) > 16:
            raise SceneError("capacity_exceeded")
        ids = []
        for catalog, region in expanded:
            self._ids[catalog] = self._ids.get(catalog, 0) + 1
            ident = f"{catalog}_{self._ids[catalog]}"
            entry = CATALOG[catalog]
            self.instances.append(dict(id=ident, catalog_id=catalog, region=region,
                                       slot=self._slot(region), container_id=None,
                                       japanese=entry.japanese, reading=entry.reading, english=entry.english))
            ids.append(ident)
        regions = {region for _, region in expanded}
        return self._event("spawned", "sensei", ids, turn_id,
                           destination=next(iter(regions)) if len(regions) == 1 else None)

    def select(self, ident: str, turn_id: int) -> dict[str, Any]:
        if self.find(ident) is None:
            raise SceneError("unknown_object")
        self.selected_id = ident
        return self._event("selected", "player", [ident], turn_id, mutates=False)

    def move(self, ident: str, region: str | None, container_id: str | None,
             actor: str, turn_id: int, purpose: str | None = None) -> dict[str, Any]:
        if (region is None) == (container_id is None):
            raise SceneError("invalid_arguments")
        if region is not None and (not isinstance(region, str) or region not in REGIONS):
            raise SceneError("invalid_arguments")
        obj = self.find(ident)
        if obj is None:
            raise SceneError("unknown_object")
        container = self.find(container_id) if container_id is not None else None
        if container_id is not None and container is None:
            raise SceneError("unknown_object")
        if container is not None and (not CATALOG[container["catalog_id"]].container
                or CATALOG[obj["catalog_id"]].container or ident == container_id
                or obj["container_id"] == container_id):
            raise SceneError("invalid_destination")
        if container_id is None and obj["container_id"] is None and obj["region"] == region:
            raise SceneError("invalid_destination")
        origin = obj["container_id"] or obj["region"]
        obj["container_id"] = container_id
        obj["region"] = container["region"] if container is not None else region
        if container is None and region in PRACTICE:
            obj["slot"] = self._slot(region)
        affected = [ident]
        if container is None:
            for inner in self.instances:
                if inner["container_id"] == ident:
                    inner["region"] = region
                    affected.append(inner["id"])
        return self._event("moved", actor, affected, turn_id, origin, container_id or region,
                           purpose if actor == "sensei" else None)
