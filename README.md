<div align="center">

🌐 &nbsp;**Polski**&nbsp; · &nbsp;[English](#exo-proxy--english)

# EXO PROXY

**Bezzałogowa sonda. Ekran CRT. Planeta, na którą nie wolno ci patrzeć bez filtra.**

Jesteś Operatorem bezzałogowej sondy kosmicznej — jedynym człowiekiem przydzielonym do patrolowania i ekstrakcji na terenie Sektorów A-L. Twoje jedyne okno na świat to terminal i celowo przytępiony podgląd z kamery sondy **SR-74**. Wykonujesz zlecenia Konsorcjum **SUIRDC**, zbierasz **proks** i czytasz pocztę, która z każdym SOLem coraz gorzej trzyma się oficjalnej wersji.

**.NET 10** · **C# 13** · **Windows** · **Terminal 180×40** · **status: alpha**

</div>

---

## Czym to jest

Wiele lat po lądowaniu *Arki Nadziei*, Konsorcjum SUIRDC wydobywa na **Cyrze-6** proks - płynny metal na napędy rakietowe i głowice — i do patrolowania poszczególnych sektorów przydziela jednego operatora tj. **Everymana**. Jesteś jednym z nich, osamotniony na najbliższe 365 SOLi.

Nie stoisz na powierzchni. Siedzisz przy terminalu i przez opóźnione łącze prowadzisz bezzałogową sondę **SR-74** — jeździsz po siatce, włączasz sensory, wydobywasz złoża, dokujesz do bazy, żeby doładować baterię i dociągnąć do kolejnego SOLa. Twój podgląd jest z założenia „nudny": to **filtr SRC**. Jeśli na ekranie mignie błękit bądź złoto, musisz zamknąć oczy — to znaczy, że filtr się zepsuł, a na tę planetę nie wolno patrzeć wprost.

Między zleceniami przychodzi poczta. Zespół Wellness gratuluje „bezpiecznego przybycia", dział historii wspomina „z wdziękiem wygaszone" miasto Aethelgard, którego pierwsze pokolenie robotników „postanowiło zostać na zawsze".

## Co znajduje się w środku

- **Sekwencja bootowania** — CRT warmup, nagłówek BIOS, logowanie, handshake Q-Link
- **Baza operatora** — nawigowalne moduły: `HUB`, `MISSION`, `MEMORY`, `COMMS`, `SETTINGS`, `UPGRADE`, `DIAG`
- **Teren** — mapa sondy rysowana znakami, sektory zwiadowcze, wysokościomierz, kamera podążająca za SR-74
- **Sensory** — `MASS` (topografia), `THERMAL` (złoża proksu), `EM` (sygnały); niezależne, każdy dokłada zużycie baterii
- **Minigry ekstrakcji** — rytmiczne wydobycie `THERMAL` (śledź żyłę i uderzaj) oraz `EM` (strojenie skaczącej częstotliwości)
- **Konwój zaopatrzenia** — bezzałogowy wagonik krążący po torze; zsynchronizuj się, by odsłonić ukryte złoże albo podjechać kilka kratek za darmo
- **Bateria i teren** — koszt jazdy rośnie z nachyleniem; upadek na niski teren uszkadza kadłub
- **Permadeath** — rozbicie lub rozładowanie w polu kończy grę operatora na stałe
- **System SOL** — dni misji filtrują skrzynkę COMMS i okna sygnałów EM

## ▶ Zagraj

Pobierz najnowszą paczkę `.exe` z **[Releases](https://github.com/WrublTop/Exo-Proxy/releases)** i uruchom. Nie wymaga instalacji .NET.

> [!NOTE]
> Gotowa paczka pojawia się z pierwszym wydaniem (release). Do tego czasu zbuduj ze źródeł (niżej).

> [!WARNING]
> Gra wymaga terminala **180×40 znaków** z obsługą kolorów ANSI. W mniejszym oknie wstrzymuje się i pokazuje ekran „dopasuj rozmiar". Zalecany Windows Terminal.

## 🔧 Zbuduj ze źródeł

Wymaga **.NET 10 SDK**.

```bash
git clone https://github.com/WrublTop/Exo-Proxy.git
cd Exo-Proxy
dotnet run
```

Zbudowanie własnej paczki `.exe` (self-contained — bez potrzeby .NET u gracza):

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Jak się gra

Z `HUB` wchodzisz w moduły: przejrzyj pocztę w `COMMS`, ułóż pliki w `MEMORY`, ulepsz sondę w `UPGRADE` albo wyrusz w teren przez `MISSION`.

Wyprawa to pętla: włącz sensory, przeczesz siatkę w poszukiwaniu złóż i sygnałów, wydobądź je (`SR COLLECT`) i **wróć do bazy, zanim padnie bateria albo kadłub**. Dokowanie (`DOCK? Y`) kończy SOL i zapisuje obecny stan rozgrywki. Każdy sensor i każde nachylenie terenu kosztuje energię, upadek z wysokości uszkadza sondę, a rozbicie albo rozładowanie w polu to koniec — na stałe.

## Komendy

**HUB**

| Komenda | Działanie |
|---|---|
| `MISSION` | Wyjazd w teren |
| `MEMORY` | Zarządzanie plikami / zapisy |
| `COMMS` | Korespondencja i wiadomości |
| `UPGRADE` | Ulepszenia sondy |
| `DIAG` | Diagnostyka sondy |
| `/HELP` | Panel pomocy |
| `/SETTINGS` | Ustawienia systemu |
| `LOGOUT` / `EXIT` | Wyloguj / wyłącz terminal |

**MISSION (uplink SR-74)**

| Komenda | Działanie |
|---|---|
| `SR MOVE N\|S\|E\|W [1-99]` | Jazda w kierunku o N kratek |
| `SR MASS\|THERMAL\|EM ON\|OFF` | Przełącz sensor |
| `SR COLLECT` | Ekstrakcja złoża pod sondą |
| `SR MARK <NAZWA>` / `SR MARKS` | Postaw / wypisz znaczniki |
| `SR ZOOM 1-3` | Skala mapy |
| strzałki | Pojedynczy krok |

## Stack techniczny

.NET 10 · C# 13 · surowy ANSI · [YamlDotNet](https://github.com/aaubry/YamlDotNet) (treść i zapisy) · [NAudio](https://github.com/naudio/NAudio) (dźwięk)

## Zespół

| Osoba | Rola |
|---|---|
| **Maciek** | Lead Programming · Lead UI · Narration Assistant |
| **Radek** | Lead Narration |
| **Adam** | Programming Assistant |
| **Maja** | Narration Assistant · QA · UI Assistant |

## Licencja

© 2026 Exo Proxy. Wszelkie prawa zastrzeżone.

---

<div align="center">

# EXO PROXY — English

🌐 &nbsp;[Polski](#exo-proxy)&nbsp; · &nbsp;**English**

**An uncrewed rover. A CRT screen. A planet you're not allowed to look at without the filter.**

You are the operator of an uncrewed space rover — the only human assigned to patrol and extraction across Sectors A-L. Your one window on the world is a terminal and the deliberately dulled camera feed of rover **SR-74**. You carry out contracts for the **SUIRDC** Consortium, collect **proks**, and read mail that holds together with the official story a little worse every sol.

**.NET 10** · **C# 13** · **Windows** · **Terminal 180×40** · **status: alpha**

</div>

## What it is

Many years after the *Ark of Hope* made landfall, the SUIRDC Consortium mines **proks** on **Cyra-6** — a liquid metal for rocket drives and warheads — and assigns a single operator, an **Everyman**, to patrol each sector. You are one of them, alone for the next 365 sols.

You never stand on the surface. You sit at a terminal and pilot the uncrewed rover **SR-74** over a laggy uplink — driving the grid, running sensors, extracting deposits, docking at base to recharge and last another sol. Your feed is "boring" by design: the **SRC filter**. If blue or gold ever flickers on screen, you're to close your eyes — it means the filter has failed, and this planet is not to be looked at directly.

Between contracts, the mail arrives. The Wellness Team congratulates you on your "safe arrival"; the history desk fondly recalls the "gracefully decommissioned" city of Aethelgard, whose first generation of workers "chose to remain permanently." The longer you read, the worse those words fit what the rover sees in the field.

## What's inside

- **Boot sequence** — CRT warmup, BIOS header, login, Q-Link handshake
- **Operator base** — navigable modules: `HUB`, `MISSION`, `MEMORY`, `COMMS`, `SETTINGS`, `UPGRADE`, `DIAG`
- **Terrain** — glyph-drawn rover map, survey sectors, altimeter, camera tracking SR-74
- **Sensors** — `MASS` (topography), `THERMAL` (proks deposits), `EM` (signals); independent, each adds battery drain
- **Extraction minigames** — rhythmic `THERMAL` (track the vein & strike) and `EM` (tune a hopping frequency) mining
- **Supply convoy** — an uncrewed runner looping a track; sync with it to reveal a hidden vein or hitch a free ride for a few tiles
- **Battery & terrain** — drive cost scales with slope; falling onto low ground damages the hull
- **Permadeath** — a wreck or a dead battery in the field ends the operator's run for good
- **SOL system** — mission days filter the COMMS inbox and EM signal windows

## ▶ Play

Download the latest `.exe` package from **[Releases](https://github.com/WrublTop/Exo-Proxy/releases)** and run it. No .NET install required.

> [!NOTE]
> The packaged build ships with the first release. Until then, build from source (below).

> [!WARNING]
> The game needs a **180×40 character** terminal with ANSI color support. In a smaller window it pauses and shows a "resize" screen. Windows Terminal recommended.

## 🔧 Build from source

Requires **.NET 10 SDK**.

```bash
git clone https://github.com/WrublTop/Exo-Proxy.git
cd Exo-Proxy
dotnet run
```

Build your own self-contained `.exe` (no .NET needed on the player's machine):

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## How you play

From `HUB` you enter the modules: read mail in `COMMS`, organize files in `MEMORY`, upgrade the rover in `UPGRADE`, or head into the field via `MISSION`.

A field run is a loop: switch on sensors, sweep the grid for deposits and signals, extract them (`SR COLLECT`), and **get back to base before your battery or hull gives out**. Docking (`DOCK? Y`) ends the sol and saves your progress. Every sensor and every slope costs power, a fall damages the rover, and a wreck or a dead battery in the field is the end — permanently.

## Commands

**HUB**

| Command | Action |
|---|---|
| `MISSION` | Head into the field |
| `MEMORY` | File management / saves |
| `COMMS` | Correspondence and messages |
| `UPGRADE` | Rover upgrades |
| `DIAG` | Rover diagnostics |
| `/HELP` | Help panel |
| `/SETTINGS` | System settings |
| `LOGOUT` / `EXIT` | Log out / power down |

**MISSION (SR-74 uplink)**

| Command | Action |
|---|---|
| `SR MOVE N\|S\|E\|W [1-99]` | Drive N cells in a direction |
| `SR MASS\|THERMAL\|EM ON\|OFF` | Toggle a sensor |
| `SR COLLECT` | Extract the deposit under the rover |
| `SR MARK <NAME>` / `SR MARKS` | Set / list markers |
| `SR ZOOM 1-3` | Map scale |
| arrow keys | Single step |

## Tech stack

.NET 10 · C# 13 · raw ANSI · [YamlDotNet](https://github.com/aaubry/YamlDotNet) (content & saves) · [NAudio](https://github.com/naudio/NAudio) (audio)

## Team

| Person | Role |
|---|---|
| **Maciek** | Lead Programming · Lead UI · Narration Assistant |
| **Radek** | Lead Narration |
| **Adam** | Programming Assistant |
| **Maja** | Narration Assistant · QA · UI Assistant |

## License

© 2026 Exo Proxy. All rights reserved.
