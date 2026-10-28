#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
art="$repo/unity/RedPandaSensei/Assets/Art"
mkdir -p "$art/Objects" "$art/Sensei" "$art/Resources/Objects" "$art/Resources/Sensei"
for source in "$repo"/assets/sprites/objects/*_card.png; do
  [[ -f "$source" ]] || continue
  name=$(basename "$source")
  cmp -s "$source" "$art/Objects/$name" || cp "$source" "$art/Objects/$name"
  cmp -s "$source" "$art/Resources/Objects/$name" || cp "$source" "$art/Resources/Objects/$name"
done
for source in "$repo"/assets/sprites/sensei/*.png; do
  [[ -f "$source" ]] || continue
  name=$(basename "$source")
  cmp -s "$source" "$art/Sensei/$name" || cp "$source" "$art/Sensei/$name"
  cmp -s "$source" "$art/Resources/Sensei/$name" || cp "$source" "$art/Resources/Sensei/$name"
done
