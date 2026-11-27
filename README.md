# ZhkElectricityBot — «Пан Амперій ⚡»

Telegram bot «Пан Амперій» (C# / .NET 10) for the residents of ЖК «Компаньйон»: the LOE outage schedule for one group and the
state of the backup power (Deye inverters + batteries). Speaks **Ukrainian only**.

## Features

| # | Feature | Who gets it |
|---|---|---|
| 1 | **Outage schedule** (`schedule`) — LOE schedule for `Schedule:Group` | everyone who pressed Start |
| 2 | **Station «ЖК «Компаньйон»»** (`station:complex`) | everyone who pressed Start |
| 3 | **Private station** (`station:home`, `OwnerOnly`) | only the owner (`Bot:OwnerUsername`) |

Each user can switch features on/off in `/settings`.

### 1 — Outage schedule

- Every check reads `api.loe.lviv.ua/api/menus?type=photo-grafic` (the JSON behind poweron.loe.lviv.ua, items
  `Today` / `Tomorrow`), always with `Cache-Control: no-cache`.
- A day that appears for the first time **with outages** → «🗓 Графік вимкнень на …». A day without outages is
  remembered silently.
- Any change of the group's intervals → «✏️ Графік змінився» (was / now). A new «станом на» time without changes is silent.
- `ReminderMinutes` (15) before every outage → «⏰ Через 15 хв вимкнуть світло». Adjacent slots
  (20:30–24:00 + 00:00–02:00) count as one outage.
- Known days and sent reminders are stored in SQLite → restarts/redeploys never repeat a message.

### 2, 3 — Stations (identical logic)

Read from the Deye Cloud Open API: `/station/latest` (SOC, load, battery/grid power, update time) and
`/device/latest` for the inverters (grid phase voltages, rated battery capacity).

- **Grid detection:** every `GridVoltageL1/L2/L3` (or `GridVoltageL1L2`) above `GridVoltageThreshold` (150 V) →
  city power; otherwise the station runs on batteries. Without voltages it falls back to grid/battery power.
- **On → off:** «🔋 зникло світло» with charge, load, autonomy forecast and when power returns per schedule.
- **Off → on:** «💡 світло повернулося» with the outage length and how much charge was used.
- **Every `StatusIntervalMinutes` (60) during an outage:** charge (and drop since the start), load, autonomy,
  expected end of the outage, and whether the charge will last.
- **Warnings** at `WarnSocLevels` (30 %, 10 %), once per outage each (the status due at the same moment is skipped).
- **No data** for `StaleDataMinutes` (30) — stale data in the cloud or Deye Cloud not answering → one «📡 немає даних від станції», and one message when it's back.
- **One message for all stations:** power lost, hourly status and power back usually happen to both stations within
  a minute or two, so a user who follows both gets **one** message — a section per station (`Order`: the apartment 🏠
  first, then the ЖК 🏢). The message waits until both stations have the event, at most `CombineWindowMinutes` (5);
  further apart — separately. A user with one station gets it immediately.
- Every station section starts with two lines: the object (`Icon` + `Title`) and the event.
- First reading after the first deploy only remembers the state (no message). The outage state is stored, so an outage
  in progress survives a redeploy.

**Autonomy forecast.** The inverter is assumed to shut off at `ReserveSocPercent` (10 %). From the real discharge
since the outage started: `hours left = (SOC − 10) / (SOC drop / hours without power)` — e.g. −22 % in 1 h at 78 % → ~3 h.
A short peak of load doesn't skew it. Until the outage lasts `MinForecastMinutes` (15) and the charge dropped
`MinForecastSocDrop` (2 %), it uses the current load: `(SOC − 10) × capacity / load` (the larger of load and battery
power — right after the switch the cloud still shows ~0 W battery power).
Capacity comes from the API: `BatteryRatedCapacity` (Ah) × nominal voltage (LiFePO4, 16 cells per module × 3.2 V,
derived from the measured voltage) — override with `Stations:N:CapacityKwh`.

**Verdict** — margin = when the batteries run out − when power returns per schedule:
more than `SafeMarginMinutes` (60) → «✅ Заряду вистачає до кінця вимкнення»; 0…60 min → «⚠️ Заряду може не вистачити…»;
below 0 → «🚨 Заряду не вистачає…». Shown in every on-battery message (lost, hourly, warnings, /status) when the outage
is on the schedule.

**Expected outage end** comes from the schedule: a scheduled outage that starts within an hour or is under way
(power can go early, switching on can take up to an hour). None → «вимкнення поза графіком».

### Polling — clock-aligned

All requests (LOE + Deye) run in one loop:

- normally every `IdleIntervalMinutes` (5) at xx:00, xx:05, xx:10 …;
- from `ActiveBeforeOutageMinutes` (60) before a scheduled outage until `ActiveAfterOutageMinutes` (60) after it,
  while any station runs on batteries, and for an hour after power returns — every `ActiveIntervalMinutes` (1)
  at the start of the minute.

## Commands

| Command | |
|---|---|
| `/start` | subscribe; welcome + current status |
| `/status` | what the user's inverters do now (apartment, then ЖК): grid or batteries, charge, load, charging power, autonomy; «немає звʼязку з інвертором» when Deye Cloud can't be read |
| `/schedule` | schedule for today/tomorrow |
| `/settings` | switch features on/off |
| `/stop` (or blocking the bot) | unsubscribe |

## Settings — `src/ZhkElectricityBot/appsettings.json`

Everything non-secret is there (env vars override, e.g. `Polling__IdleIntervalMinutes=5`):

| Section | Keys |
|---|---|
| `Bot` | `TimeZone` (Europe/Kyiv), `DatabasePath` |
| `Polling` | `IdleIntervalMinutes`, `ActiveIntervalMinutes`, `ActiveBeforeOutageMinutes`, `ActiveAfterOutageMinutes` |
| `Schedule` | `Enabled`, `Url`, `SiteUrl`, `Group` (3.1), `ReminderMinutes` |
| `Deye` | `BaseUrl` (eu1) |
| `Battery` | `StatusIntervalMinutes`, `WarnSocLevels`, `ReserveSocPercent`, `GridVoltageThreshold`, `MinForecastMinutes`, `MinForecastSocDrop`, `StaleDataMinutes`, `SafeMarginMinutes`, `CombineWindowMinutes`, `NominalCellVoltage` |
| `Stations` | `Key`, `Title`, `Icon`, `Order`, `OwnerOnly`, `CapacityKwh` (0 = from API); `StationId` comes from secrets |

## Secrets (GitHub → Settings → Secrets and variables → Actions)

| Secret | Value |
|---|---|
| `BOT_TOKEN` | token from @BotFather |
| `OWNER_USERNAME` | owner's Telegram username (with or without `@`) |
| `DEYE_APP_ID`, `DEYE_APP_SECRET` | from developer.deyecloud.com |
| `DEYE_EMAIL`, `DEYE_PASSWORD` | Deye Cloud account (the password is SHA-256 hashed by the bot) |
| `DEYE_COMPLEX_STATION_ID` | Deye station id of the ЖК |
| `DEYE_HOME_STATION_ID` | Deye station id of the owner-only station |
| `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY` | the VM (same format as other bots on it) |

Nothing private is committed: no tokens, ids, addresses or usernames.

## Run locally

```powershell
cd src/ZhkElectricityBot
dotnet user-secrets set "Bot:Token" "<token>"
dotnet user-secrets set "Bot:OwnerUsername" "<username>"
dotnet user-secrets set "Deye:AppId" "<appId>"
dotnet user-secrets set "Deye:AppSecret" "<appSecret>"
dotnet user-secrets set "Deye:Email" "<email>"
dotnet user-secrets set "Deye:Password" "<password>"
dotnet user-secrets set "Stations:0:StationId" "<complex station id>"
dotnet user-secrets set "Stations:1:StationId" "<home station id>"
dotnet run                 # the bot
dotnet run -- --dry-run    # print schedule + station readings and sample messages; nothing is sent or stored
```

> Only one copy can poll Telegram at a time — stop the server container first, or you get `409 Conflict`.

## Deployment

Every push to `main` → GitHub Actions builds a Docker image (amd64 + arm64), pushes it to GHCR, SSHes into the VM
and restarts the container `zhkelectricitybot`. State (subscribers, settings, known schedules, outage progress)
lives in SQLite in the `zhkelectricitybot-data` volume and survives redeploys. A new VM: `deploy/setup-vm.sh`.

```powershell
ssh -i $KEY $VM "docker logs --tail 100 zhkelectricitybot"
ssh -i $KEY $VM "docker restart zhkelectricitybot"
"SELECT * FROM subscribers;" | ssh -i $KEY $VM "docker run --rm -i -v zhkelectricitybot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"
```

## Bot profile

Synced automatically on every start (only when something changed):

- name, short description, description and the command menu — from `BotTexts.cs`;
- profile photo — `assets/avatar.jpg` is embedded into the build; its SHA-256 is stored in `bot_state`, so the photo is
  uploaded once after it changes (`setMyProfilePhoto`). To change the photo: replace `assets/avatar.jpg`, push.

Set once in @BotFather → Edit Bot → Bot Settings: **Allow Groups** = off.

## Project layout

| Path | What |
|---|---|
| `src/ZhkElectricityBot/BotTexts.cs` | all Ukrainian texts |
| `src/ZhkElectricityBot/Schedule/` | LOE client, parser, schedule feature |
| `src/ZhkElectricityBot/Stations/` | Deye client, readings, station features, forecast |
| `src/ZhkElectricityBot/Bot/` | polling, commands, features/access, broadcaster |
| `src/ZhkElectricityBot/PowerMonitorService.cs` | clock-aligned 5 / 1-minute loop |
| `src/ZhkElectricityBot/Storage/` | SQLite: subscribers, feature settings, state |
| `assets/` | avatar (svg source → png / jpg; the jpg is uploaded by the bot) |
| `deploy/`, `.github/workflows/deploy.yml` | VM setup, deploy script, CI |
