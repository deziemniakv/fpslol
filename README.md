# FPS.LOL

**Optimize. Play. Perform.**

FPS.LOL to natywna aplikacja Windows (C# / .NET 8 / WPF, x64) do bezpiecznej i w pełni odwracalnej
optymalizacji Windows pod granie. Zmienia wyłącznie rzeczywiste, udokumentowane ustawienia systemu,
każdą zmianę weryfikuje po zastosowaniu i zapisuje w Restore Center, skąd można ją cofnąć.

FPS.LOL **nie** obiecuje „+500 FPS”. Poprawia to, co realnie da się poprawić: stabilność frame-time,
obciążenie w tle, opóźnienia wejścia i sieć — bez overclockingu, sterowników i wyłączania zabezpieczeń.

---

## Funkcje

| Sekcja | Co robi |
|---|---|
| **Dashboard** | CPU (model, użycie, zegar), GPU (model, użycie, temperatura, VRAM), RAM, system, status gamingowy, **System Score** liczony z realnych ustawień (klik → szczegółowa analiza). |
| **Performance** | Wykresy CPU/GPU/RAM na żywo; **licznik FPS / frame time / 1% low** z ETW (zdarzenia Present DXGI/D3D9 — ta sama publiczna metoda co PresentMon; bez wstrzykiwania i sterowników). |
| **Optimize** | One-click: skan → lista zmian → wybór → backup + punkt przywracania → zastosowanie → weryfikacja → wynik (Success / Failed / Skipped / Not supported) + informacja o restarcie. |
| **Tweaks** | 15 tweaków z opisem, kategorią, stanem aktualnym/zalecanym, poziomem ryzyka (SAFE/MODERATE/ADVANCED), wymogiem restartu/admina, przyciskami **Apply** i **Restore**. |
| **Gaming** | Status (Game Mode, HAGS, Game Bar, nagrywanie w tle, plan zasilania) + **profile gier**: wykrywanie Steam / Epic / Riot, priorytet CPU, preferencja GPU, fullscreen optimizations. |
| **Power** | Aktywny plan, lista planów, przełączanie, profil gamingowy, przywrócenie poprzedniego planu, dodanie Ultimate Performance (nigdy nie usuwa cudzych planów). |
| **Network** | Karta sieciowa (link speed, IPv4/IPv6, bramka, DNS, MAC), test opóźnień i utraty pakietów, konfiguracja DNS (Automatic/Cloudflare/Google/Quad9), flush DNS, uzasadnione tweaki sieciowe. |
| **Processes** | Lista procesów (CPU, RAM, PID), sortowanie, wyszukiwanie, kończenie z potwierdzeniem. Procesy **SYSTEM**/**PROTECTED** są zablokowane. |
| **Startup** | Programy autostartu (rejestr + foldery Startup), wydawca, ścieżka, status; włącz/wyłącz dokładnie jak Menedżer zadań — **bez usuwania wpisów**. |
| **Cleanup** | Najpierw **Scan** → „X GB can be cleaned” → dopiero **Clean**. Tylko regenerowalne dane z predefiniowanych folderów. |
| **Restore** | Historia wszystkich zmian (data, ustawienie, `przed → po`), Restore pojedynczo / zaznaczone / wszystko, backupy konfiguracji, skrót do Przywracania systemu Windows. |
| **Settings** | Autostart, tray, powiadomienia, intensywność Liquid Glass, blur (acrylic), intensywność animacji, Reduce animations, pytanie przed zmianą, restore point, logowanie, debug, podgląd logów, About. |

### Lista tweaków

| Tweak | Kategoria | Ryzyko | Uwagi |
|---|---|---|---|
| Game Mode | Windows Gaming | SAFE | `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` |
| Background recording | Windows Gaming | SAFE | „Record what happened” wyłączone, ręczne nagrywanie działa |
| Game DVR capture | Windows Gaming | MODERATE | wyłącza haki przechwytywania (zrzuty/klipy Game Bar przestają działać) |
| Xbox Game Bar controller shortcut | Windows Gaming | SAFE | przycisk Xbox nie otwiera nakładki |
| Enhance pointer precision | Input | SAFE | akceleracja myszy przez `SystemParametersInfo` |
| Sticky/Filter/Toggle Keys shortcuts | Input | SAFE | wyłącza tylko skróty, nie funkcje ułatwień dostępu |
| Hardware-accelerated GPU scheduling | GPU | MODERATE | wsparcie odczytywane **ze sterownika** (D3DKMT WDDM 2.7 caps); wymaga restartu i admina |
| Optimizations for windowed games | GPU | SAFE | Windows 11 22H2+, flip model dla DX10/11 w oknie |
| High performance power plan | Power | SAFE | aktywuje istniejący plan wydajności lub tworzy kopię szablonu |
| Background apps | Background Processes | MODERATE | aplikacje Store w tle |
| Delivery Optimization peer sharing | Privacy & Background Services | SAFE | bez uploadu aktualizacji do innych PC; Windows Update działa |
| Connected User Experiences and Telemetry | Privacy & Background Services | ADVANCED | usługa DiagTrack → Disabled (przez SCM) |
| Window animations | Visual Effects | SAFE | nie zmienia FPS w grach — uczciwie opisane |
| TCP receive window auto-tuning | Network | SAFE | **naprawia** „normal”, zepsute przez stare skrypty |
| Network adapter power saving | Network | SAFE | karta nie jest usypiana przez Windows |

Czego FPS.LOL **celowo nie robi**: nie wyłącza Windows Defendera, Windows Update ani zabezpieczeń
(VBS/HVCI), nie usuwa usług ani kluczy rejestru, nie stosuje „TCP gaming tweaks” (Nagle, NetworkThrottlingIndex)
bez udowodnionego efektu, nie pobiera skryptów z internetu, nie instaluje sterowników jądra.

---

## Bezpieczeństwo — jak działa silnik zmian

Każda zmiana to transakcja (`Optimizations/TransactionRunner.cs`):

1. **Walidacja** – `SafetyPolicy` ma twardą allow-listę kluczy rejestru i usług. Wszystko spoza niej jest odrzucane
   (także w procesie z uprawnieniami administratora).
2. **Snapshot** – zapis dokładnej poprzedniej wartości (albo informacji, że wartość nie istniała).
3. **Backup** – przed każdym zestawem zmian zapisywany jest plik `backups\backup-*.json`
   (niezależny od historii i od punktów przywracania Windows). Opcjonalnie: punkt przywracania systemu.
4. **Wykonanie** i **weryfikacja** – wartość jest odczytywana ponownie; brak efektu = błąd.
5. **Rollback** – przy błędzie wszystkie wykonane kroki są cofane w odwrotnej kolejności.
6. **Historia** – udane zmiany trafiają do Restore Center (`history.json`) z kompletem snapshotów.

Wynik jest zawsze jednym z: **Success / Failed / Skipped / Not supported** — aplikacja nigdy nie raportuje sukcesu bez weryfikacji.

### Uprawnienia administratora

FPS.LOL działa jako zwykły użytkownik (`asInvoker`). Gdy operacja wymaga admina, uruchamiany jest
krótkotrwały proces pomocniczy (`FPS.LOL.exe --elevated-worker`) — **jedno okno UAC na cały zestaw zmian**.
Proces łączy się przez named pipe o losowej nazwie, obie strony weryfikują PID drugiej strony, a zadania
przechodzą przez tę samą `SafetyPolicy`. Po wykonaniu zadań proces kończy działanie.
Opcjonalnie można uruchomić całą aplikację jako administrator (sidebar → *Restart as administrator*),
co jest potrzebne np. do licznika FPS (ETW).

---

## Wymagania

- Windows 10 (1809+) lub Windows 11, x64
- Do uruchomienia opublikowanej wersji: **nic** (build jest self-contained)
- Do budowania: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Efekt blur (acrylic) wymaga Windows 11 build 22523+; na starszych systemach używane jest tło nieprzezroczyste.

## Budowanie

```powershell
dotnet build FPS.LOL.sln -c Release
```

Konfiguracja Release: `Optimize=true`, x64, ReadyToRun przy publikacji. Wszystko w jednym kroku (build + testy + publish):

```powershell
.\build.ps1
```

## Uruchomienie

Z kodu źródłowego:

```powershell
dotnet run --project src\FpsLol\FpsLol.csproj -c Release
```

Z opublikowanej wersji: `publish\win-x64\FPS.LOL.exe`.

Przełączniki wiersza poleceń:

| Argument | Działanie |
|---|---|
| `--minimized` | start w zasobniku (używane przez „Start with Windows”) |
| `--page <Nazwa>` | otwiera wskazaną stronę, np. `--page Tweaks` (przydatne do testów) |
| `--welcome` | ponownie pokazuje ekran powitalny |

## Publikowanie jako Windows x64

```powershell
dotnet publish src\FpsLol\FpsLol.csproj -p:PublishProfile=win-x64
```

Wynik: `publish\win-x64\` — samodzielny folder (runtime .NET w środku, ReadyToRun, bez symboli debug).
Folder można spakować do ZIP i uruchomić na dowolnym Windows x64 bez instalowania .NET.
Profil: `src\FpsLol\Properties\PublishProfiles\win-x64.pubxml`.

## Testy

```powershell
dotnet test tests\FpsLol.Tests\FpsLol.Tests.csproj -c Release
dotnet test tests\FpsLol.Tests\FpsLol.Tests.csproj -c Release --filter "Category!=System"   # tylko logika, bez dotykania systemu
```

- **Testy jednostkowe** – allow-lista `SafetyPolicy`, rollback transakcji, parsowanie, deterministyczność score.
- **Testy systemowe** (`Category=System`) – na prawdziwym Windows: zmiana wartości rejestru, ustawienia SPI
  i planu zasilania → weryfikacja → przywrócenie → weryfikacja; serializacja snapshotów; test end-to-end
  kanału procesu pomocniczego (wykonanie zadania, odrzucenie zadania zablokowanego przez politykę,
  odrzucenie serwera z fałszywym PID). Nie wymagają admina i zostawiają system w stanie wyjściowym.

### Przykładowy workflow testowania ręcznego

1. **Pierwsze uruchomienie** – `FPS.LOL.exe --welcome` → *Start system scan* → sprawdź wykryte CPU/GPU/RAM/Windows/plan → wynik score.
2. **Dashboard** – porównaj użycie CPU/GPU/RAM z Menedżerem zadań; kliknij pierścień score → analiza punktów.
3. **Tweak bez admina** – Tweaks → *Window animations* → Apply → potwierdź → toast „Applied and verified”
   → sprawdź w *sysdm.cpl → Zaawansowane → Wydajność*. Potem Restore Center → Restore → wartość wraca.
4. **Tweak z adminem** – Network → *TCP auto-tuning* lub *Network adapter power saving* → Apply → pojawia się **jedno** okno UAC.
   Odmowa UAC → wynik „Skipped: Administrator permission was declined”, nic nie jest zmieniane.
5. **One-click** – Optimize → Scan system → zaznacz/odznacz → Apply selected → potwierdzenie z listą zmian
   → wynik z liczbą Success/Failed/Skipped i score przed/po. Restore Center pokazuje wszystkie zmiany + backup.
6. **Procesy** – spróbuj zakończyć `explorer` lub `svchost` → komunikat o ochronie; zakończ np. Notatnik → potwierdzenie → proces znika.
7. **Startup** – wyłącz wpis → sprawdź w Menedżerze zadań (Autostart) że jest „Wyłączony” → włącz ponownie.
8. **Cleanup** – Scan → porównaj rozmiary → Clean → wynik (pliki w użyciu są pomijane).
9. **FPS** – uruchom FPS.LOL jako administrator, włącz grę DirectX, Performance → wybierz proces → Start capture.
10. **Logi** – Settings → Activity log / *Open logs folder* (`%LOCALAPPDATA%\FPS.LOL\logs`).

---

## Architektura

```
FPS.LOL/
├─ FPS.LOL.sln
├─ build.ps1
├─ src/FpsLol/
│  ├─ Program.cs               punkt wejścia: single instance, tryb elevated worker, przełączniki
│  ├─ App.xaml(.cs)            kontener DI, obsługa wyjątków, motyw
│  ├─ Views/                   MainWindow (bezramkowe okno), strony, dialogi, ekran powitalny
│  ├─ ViewModels/              MVVM (CommunityToolkit.Mvvm) — jedna klasa na stronę
│  ├─ Models/                  modele domenowe (tweaki, zmiany, sprzęt, gry, wyniki operacji)
│  ├─ Services/                ustawienia, historia, backupy, ChangeService, TweakEngine, score, procesy,
│  │                           autostart, cleanup, zasilanie, gry, nawigacja, dialogi, toasty, tray, motyw
│  ├─ Optimizations/           SystemAction/ActionSnapshot, TweakCatalog, SafetyPolicy, TransactionRunner
│  │  └─ Privileged/           protokół i proces pomocniczy z uprawnieniami administratora
│  ├─ SystemIntegration/       P/Invoke, rejestr, SPI, plany zasilania, usługi, punkty przywracania, ochrona procesów
│  ├─ Hardware/                DXGI, D3DKMT, PDH, WMI, monitoring na żywo, licznik FPS (ETW)
│  ├─ Networking/              karty sieciowe, DNS, TCP, zarządzanie energią karty, ping
│  ├─ Storage/                 silnik czyszczenia
│  ├─ Logging/                 czytelne logi dzienne + podgląd w UI
│  ├─ Controls/                IconView, PageHost (przejścia), Sparkline, ScoreRing, Skeleton, Spinner, konwertery
│  ├─ Themes/                  kolory, typografia, ikony (geometria w stylu Lucide), style kontrolek, szablony
│  ├─ Utilities/               ścieżki, JSON, formatowanie, uruchamianie narzędzi systemowych
│  └─ Assets/                  ikona aplikacji
└─ tests/FpsLol.Tests/         xUnit: testy jednostkowe + systemowe
```

Zależności (tylko stabilne, utrzymywane przez Microsoft / .NET Foundation):
`CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `System.Management` (WMI),
`System.ServiceProcess.ServiceController`, `Microsoft.Diagnostics.Tracing.TraceEvent` (licznik FPS).
Wykresy, ikony, pierścień score i efekty są rysowane własnym kodem — bez bibliotek UI.

## Źródła danych

| Dane | Źródło |
|---|---|
| CPU usage / zegar | PDH `Processor Information\% Processor Utility` i `% Processor Performance` (jak Menedżer zadań) |
| GPU usage / VRAM | PDH `GPU Engine` / `GPU Adapter Memory` (dla karty wybranej przez DXGI) |
| GPU temperatura, wsparcie HAGS | D3DKMT (`ADAPTERPERFDATA`, `WDDM_2_7_CAPS`) — dane ze sterownika WDDM |
| Model GPU, VRAM całkowity | DXGI (`IDXGIFactory1`) — poprawne także dla kart > 4 GB |
| CPU, RAM, płyta, BIOS, dyski | WMI (`Win32_*`, `MSFT_PhysicalDisk`) |
| Plany zasilania | `powrprof.dll` (niezależne od języka systemu) |
| TCP auto-tuning | WMI `MSFT_NetTCPSetting` |

## Znane ograniczenia (zamiast zmyślonych wartości)

- **Temperatura CPU**: Windows nie udostępnia temperatury rdzenia bez sterownika jądra. FPS.LOL nie instaluje sterowników,
  więc pokazuje **N/A** (jako administrator — strefę termiczną ACPI, wyraźnie opisaną jako czujnik płyty).
- **Licznik FPS** wymaga uprawnień administratora (sesja ETW) i mierzy gry DirectX (DXGI/D3D9). Tytuły Vulkan/OpenGL mogą pokazać 0.
- **HAGS** – widoczny tylko, gdy sterownik GPU zgłasza wsparcie (np. część kart AMD RX 6000 go nie zgłasza).
- **Startup impact** – N/A: Windows nie udostępnia wiarygodnego pomiaru poza Menedżerem zadań.
- **Modern Standby** – na takich laptopach Windows udostępnia tylko plan Zrównoważony; FPS.LOL to wykrywa i kieruje do „Power mode”.

## Dane aplikacji

`%LOCALAPPDATA%\FPS.LOL\` — `settings.json`, `history.json` (Restore Center), `profiles.json`,
`backups\`, `logs\fpslol-RRRRMMDD.log` (proces pomocniczy: `…-elevated.log`).

Przykład logu:

```
[09:42:12] INFO System scan started
[09:42:13] INFO Windows 11 Home 25H2 (build 26200.9457) detected
[09:42:13] INFO GPU detected: AMD Radeon RX 6600
[09:42:14] INFO Game Mode: On
[09:42:14] SUCCESS Background recording: Success — Applied and verified.
```

## Credits

FPS.LOL · .NET 8 / WPF · CommunityToolkit.Mvvm · Microsoft TraceEvent · geometria ikon inspirowana [Lucide](https://lucide.dev) (ISC).
