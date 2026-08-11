# Config file reference (`config.json`)

Keysist's config file mixes two different key-naming vocabularies. They look
similar (both are strings naming a key) but come from different sources and
are used in different fields. Don't confuse them.

## 1. `mappings[].key` — `System.Windows.Forms.Keys` enum names

Each `DeviceConfig.mappings[]` entry's `key` field identifies which physical
key on the matched device triggers the mapping. This value is produced by
Keysist itself (via Discover Device, or by hand) as the string form of the
.NET `System.Windows.Forms.Keys` enum, i.e. `((Keys)vkey).ToString()`.

Examples: `"NumPad5"`, `"D5"` (the "5" key on the main row, not the numpad),
`"OemComma"`, `"F13"`, `"Escape"`.

```json
{
  "devices": [
    {
      "name": "Macro Pad",
      "devicePathMatch": "VID_1234&PID_5678",
      "defaultSwallow": true,
      "mappings": [
        { "key": "NumPad5", "action": { "type": "launch", "path": "notepad.exe" } }
      ]
    }
  ]
}
```

## 2. `sendKeys` action `steps[].value` — `VK_*` names (or hex fallback)

When an action's `type` is `"sendKeys"`, each step's `send` field is either
`"vkey"` or `"text"`. When `send` is `"vkey"`, the step's `value` is looked up
in `VirtualKeyNames` (`src/Keysist/Actions/VirtualKeyNames.cs`) — a separate,
much smaller vocabulary of named virtual keys used for synthesizing key
presses via `SendInput`, independent of anything a physical mapped key is
named.

Currently supported names:

- `VK_MEDIA_PLAY_PAUSE`
- `VK_MEDIA_NEXT_TRACK`
- `VK_MEDIA_PREV_TRACK`
- `VK_VOLUME_UP`
- `VK_VOLUME_DOWN`
- `VK_VOLUME_MUTE`

Any other value is treated as a `0x`-prefixed hex virtual-key code fallback
(e.g. `"0x5B"` for the left Windows key).

```json
{
  "type": "sendKeys",
  "steps": [
    { "send": "vkey", "value": "VK_MEDIA_PLAY_PAUSE" },
    { "send": "text", "value": "hello", "delayMs": 50 }
  ]
}
```

## Summary

| Field | Vocabulary | Source |
|---|---|---|
| `mappings[].key` | `System.Windows.Forms.Keys` names | `((Keys)vkey).ToString()` |
| `sendKeys` step `value` (when `send` is `"vkey"`) | `VK_*` names / `0x` hex fallback | `VirtualKeyNames` |

These are two different fields describing two different things (which key
triggers the mapping, vs. which key to synthesize as output) — a value valid
in one is not necessarily meaningful in the other.
