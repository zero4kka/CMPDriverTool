# CMP Driver Tool ⚡🎮
**Современная утилита для превращения майнинг-видеокарт NVIDIA CMP 30HX и CMP 40HX в полноценные игровые видеокарты.**

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Windows 11 Fluent UI](https://img.shields.io/badge/Platform-Windows_10_/_11_x64-0078D6?logo=windows&logoColor=white)](https://microsoft.com/windows)
[![Tests](https://img.shields.io/badge/Tests-39_Passed-brightgreen)](CMPDriverTool.Tests)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Patcher](https://img.shields.io/badge/Patcher-dartraiden%2FNVIDIA--patcher-success?logo=github)](https://github.com/dartraiden/NVIDIA-patcher)

---

## 📖 О проекте

**CMP Driver Tool** — это приложение с современным Fluent UI интерфейсом в стиле Windows 11, созданное для автоматизации разблокировки игрового ускорения Direct3D / Vulkan на видеокартах NVIDIA CMP (Crypto Mining Processor) **CMP 30HX** (чип TU116, аналог GTX 1660 Super) и **CMP 40HX** (чип TU106, аналог RTX 2060 Super).

Утилита автоматизирует полную цепочку: от скачивания оригинального WHQL пакета NVIDIA и его очистки от телеметрии до применения официальных правил модификации INF [dartraiden/NVIDIA-patcher](https://github.com/dartraiden/NVIDIA-patcher), тестовой цифровой подписи драйвера и чистой PnP установки.

---

## ✨ Ключевые возможности

- ⚡ **Установка драйвера в 1 клик:**
  - Автоматическая загрузка официального WHQL-драйвера NVIDIA (v552.22, v537.58, v560.94 и др.) или выбор своего `.exe` файла.
  - Распаковка архива и удаление лишней телеметрии (ShadowPlay, NodeJS, NvContainer, telemetry).
  - Модификация `nvacig.inf` / `nvami.inf` по спецификации `dartraiden`: активация Cross-Adapter Presentation, MsHybrid, Optimus Type 2, снятие майнинг-лока.
  - Автоматическая генерация тестового сертификата Code Signing и подпись драйвера (`signtool.exe` / Pnputil).
  - Чистая интеграция в хранилище драйверов Windows через `pnputil.exe` с сохранением `CatalogFile`.

- 🏷️ **Выбор имени видеокарты в системе:**
  - Возможность выбора отображаемого имени в **Диспетчере задач** (вкладка *Производительность* $\to$ *Графический процессор*), **Диспетчере устройств** и играх.
  - **Готовые пресеты для CMP 30HX:**
    - `NVIDIA GeForce GTX 1660 SUPER (Рекомендуется для игр)`
    - `NVIDIA CMP 30HX`
    - `NVIDIA CMP 30HX (Gaming Edition)`
    - `NVIDIA CMP 30HX / GTX 1660 SUPER (Gaming Edition - TU116)`
    - `✏️ Своё имя...` (произвольный ввод)
  - **Мгновенное переименование:** возможность изменить имя уже установленной видеокарты за 1 секунду без переустановки драйвера.

- 🔍 **Аппаратная диагностика PCIe:**
  - Определение конфигурации линий PCIe (x16 / x8 / x4 / x1).
  - Умное разделение: выявление физически распаянных плат с линиями x1 от видеокарт в энергосберегающем режиме PCIe ASPM (в простое).
  - Интерактивная иллюстрированная справка по допайке недостающих разделительных SMD-конденсаторов 0.1 мкФ для устранения статтеров.

- 🖥️ **Диагностика вывода изображения (Direct3D Cross-Adapter):**
  - Видеокарты CMP не имеют видеовыходов, поэтому рендеринг выполняется на CMP, а вывод изображения передается на монитор через видеовыход материнской платы (встроенную графику Intel HD Graphics / AMD Radeon Graphics) либо через вспомогательную видеокарту.
  - Утилита проверяет наличие активного адаптера вывода и выводит подсказки по настройке.

- 🧹 **Чистое удаление видеодрайверов (Clean DDU-like Uninstall):**
  - Перечисление всех установленных OEM-пакетов NVIDIA в системном хранилище `DriverStore` (`/enum-drivers`).
  - Безопасное принудительное удаление устаревших или поврежденных пакетов драйверов в 1 клик с последующим сбросом адаптера в базовый режим Windows.

- 🔄 **Перезапуск графического стека Windows:**
  - Программная отправка горячей комбинации <kbd>Win</kbd> + <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd> для сброса DWM и WDDM-драйвера без перезагрузки ПК.

- 🎥 **Опциональный пресет NVENC:**
  - Снятие искусственных ограничений NVIDIA на максимальное количество одновременных сессий стриминга и аппаратного захвата экрана NVENC/NvFBC (до 128 потоков).

---

## 🛠️ Поддерживаемые видеокарты

| Видеокарта | Архитектура | Чип | Аппаратный ID | Игровой аналог |
|---|---|---|---|---|
| **NVIDIA CMP 30HX** | Turing | TU116 | `PCI\VEN_10DE&DEV_2187` | GTX 1660 Super |
| **NVIDIA CMP 30HX** | Turing | TU116 | `PCI\VEN_10DE&DEV_2188` | GTX 1660 Super |
| **MSI CMP 30HX Miner XS 6G** | Turing | TU116 | `PCI\VEN_10DE&DEV_2189` | GTX 1660 Super |
| **NVIDIA CMP 40HX** | Turing | TU106 | `PCI\VEN_10DE&DEV_2184` | RTX 2060 Super |
| **NVIDIA CMP 40HX** | Turing | TU106 | `PCI\VEN_10DE&DEV_1F07` | RTX 2060 Super |
| **NVIDIA CMP 40HX** | Turing | TU106 | `PCI\VEN_10DE&DEV_1F0B` | RTX 2060 Super |

---

## 📋 Системные требования

1. **Операционная система:** Windows 10 x64 или Windows 11 x64 (версия 1809 или новее).
2. **UEFI Secure Boot:** Должен быть **отключен** в настройках BIOS/UEFI материнской платы (модифицированные драйверы с тестовой цифровой подписью блокируются загрузчиком Secure Boot).
3. **Тестовый режим Windows (Test Mode):** Должен быть включен (`bcdedit /set testsigning on`). Утилита умеет включать его автоматически в 1 клик.
4. **Права доступа:** Запуск от имени Администратора (приложение автоматически запрашивает UAC elevation).
5. **Вывод изображения:** Монитор должен быть подключен к материнской плате (для вывода через Intel HD / AMD Vega) либо ко второму видеоадаптеру.

---

## 🚀 Сборка из исходного кода

### Требования для сборки:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (v17.8+) или Visual Studio Code с расширением C# Dev Kit.

### Команды для сборки:
```powershell
# 1. Клонирование репозитория
git clone https://github.com/<ваш-аккаунт>/CMPDriverTool.git
cd CMPDriverTool

# 2. Восстановление зависимостей
dotnet restore CMPDriverTool.sln

# 3. Сборка решения в Release конфигурации
dotnet build CMPDriverTool.sln -c Release

# 4. Запуск всех 39 юнит-тестов
dotnet test CMPDriverTool.sln -c Release

# 5. Публикация готового исполняемого файла
dotnet publish CMPDriverTool.App/CMPDriverTool.App.csproj -c Release -r win-x64 --self-contained false -o ./bin/Publish
```

Исполняемый файл приложения будет находиться в папке `bin/Publish/CMPDriverTool.App.exe`.

---

## 📂 Структура проекта

```text
CMPDriverTool/
│
├── CMPDriverTool.sln               # Solution файл для Visual Studio / Rider / dotnet CLI
├── LICENSE                         # MIT лицензия
├── README.md                       # Документация проекта
├── .gitignore                      # Исключения для Git (.NET, Visual Studio, кэши)
│
├── .github/
│   └── workflows/
│       └── build.yml               # GitHub Actions CI: автоматическая сборка и тесты
│
├── CMPDriverTool.App/              # Клиентское приложение WPF (.NET 8 Windows)
│   ├── app.manifest                # Манифест с requireAdministrator
│   ├── App.xaml                    # Словарь стилей Windows 11 Fluent Dark
│   ├── App.xaml.cs                 # Точка входа WPF
│   ├── Converters/                 # Конвертеры привязок XAML
│   ├── ViewModels/                 # MVVM ViewModel (CommunityToolkit.Mvvm)
│   │   ├── DashboardViewModel.cs   # Главный экран диагностики и установки
│   │   ├── InstallerViewModel.cs   # 6-этапный мастер установки с live логом
│   │   ├── GamesViewModel.cs       # Автоматическая привязка игр к CMP
│   │   ├── ToolsViewModel.cs       # Переименование, удаление драйверов, Test Mode
│   │   └── MainViewModel.cs        # Навигация приложения
│   └── Views/                      # XAML представления
│       ├── DashboardView.xaml
│       ├── InstallerView.xaml
│       ├── GamesView.xaml
│       ├── ToolsView.xaml
│       └── MainWindow.xaml
│
├── CMPDriverTool.Core/             # Библиотека ядра (бизнес-логика)
│   ├── Certificate/                # Управление тестовыми сертификатами и PnP установщик
│   │   ├── CertificateManager.cs   # Создание X.509 сертификата и подпись через signtool
│   │   └── DriverInstaller.cs      # Установка INF пакета через pnputil.exe
│   ├── Downloader/                 # Загрузчик WHQL пакетов NVIDIA
│   ├── Gaming/                     # Настройка Direct3D реестра и детектор игр
│   ├── Hardware/                   # Диагностика оборудования (WMI, SetupAPI, PCIe lanes)
│   ├── Manifest/                   # Менеджер манифеста драйверов и встроенный drivers.json
│   ├── Models/                     # Модели данных (GPU info, presets, progress)
│   ├── Patcher/                    # Модуль интеграции dartraiden/NVIDIA-patcher
│   │   ├── DartraidenPatcherService.cs # Инжекция Cross-Adapter, DevID и пресетов в INF
│   │   ├── DebloatService.cs       # Очистка пакета от телеметрии
│   │   └── DriverExtractor.cs      # Распаковка установщика 7-Zip / SFX
│   └── Tools/                      # Инструменты обслуживания
│       └── SystemToolsService.cs   # Мгновенное переименование, перезапуск GPU, удаление
│
└── CMPDriverTool.Tests/            # Набор автоматических тестов (xUnit)
    ├── UnitTest1.cs                # 39 юнит-тестов (PnP IDs, PCIe, INF патчер, XAML, MVVM)
    └── CMPDriverTool.Tests.csproj
```

---

## 🤝 Благодарности

- **[dartraiden](https://github.com/dartraiden)** — за создание и поддержку репозитория [NVIDIA-patcher](https://github.com/dartraiden/NVIDIA-patcher), открывшего дорогу играм на картах CMP.
- **WPF-UI & CommunityToolkit.Mvvm** — за современные компоненты интерфейса в стиле Windows 11.

---

## 📜 Лицензия

Проект распространяется под свободной лицензией **MIT**. Подробности в файле [LICENSE](LICENSE).
