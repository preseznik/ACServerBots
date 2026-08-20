# Magione acceptance preset

This is the first bounded acceptance event: two human-only `bmw_m3_e30` slots, six fixed bots, five minutes of open practice, and a closed three-lap race. Fuel use, tyre wear, damage, lobby registration, Steam auth, and UPnP are disabled.

The committed `extra_cfg.yml` uses loopback intentionally. Run `tools/Stage-CmRaceBotServer.ps1` against a Content Manager server pack to reproduce this fixed acceptance event. For normal use, configure the event in Content Manager and run `tools/Start-CmLanRaceBots.cmd`; it snapshots the sole current CM server preset without requiring a Pack export.
