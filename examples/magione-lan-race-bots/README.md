# Magione acceptance preset

This is the first bounded acceptance event: two human-only `bmw_m3_e30` slots, six fixed bots, five minutes of open practice, and a closed three-lap race. Fuel use, tyre wear, damage, lobby registration, Steam auth, and UPnP are disabled.

The committed `extra_cfg.yml` uses loopback intentionally. Run `tools/Stage-CmRaceBotServer.ps1` against a Content Manager server pack to validate installed content, copy `fast_lane.ai` and car checksums, and write the host's private LAN address into the staged preset.
