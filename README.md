# GameLauncher

Нативный лаунчер игр для Windows 11 на WinUI 3 (.NET 10).

## Возможности

- Библиотека игр: добавление `.exe`, переименование, удаление, поиск (Ctrl+F).
- Кнопка «Играть» — запуск из папки игры (многие игры без этого не стартуют).
- Учёт времени в игре, в том числе для игр со стартером. Время считается, пока лаунчер открыт.
- Обложки и баннеры из [SteamGridDB](https://www.steamgriddb.com/) — нужен свой API-ключ (вводится в настройках, хранится зашифрованным DPAPI). Без ключа — иконка из exe.
- Страница игры: баннер, время, последний запуск, описание, жанры и скриншоты из Steam Store (на русском).
- Настройки: тема, витрина Steam для описаний, ключ SteamGridDB, папка данных. Фон Mica.

## Установка

Ставить ничего не нужно: скачайте `GameLauncher-<версия>-win-x64.zip` со страницы
[Releases](https://github.com/eva3si0n/GameLauncher/releases), распакуйте в любую папку и запустите `GameLauncher.exe`.
Приложение self-contained, требует Windows 11 x64. Подписи нет — SmartScreen при первом запуске может предупредить
(«Подробнее» → «Выполнить в любом случае»).

Данные хранятся в `%LocalAppData%\GameLauncher` — при обновлении достаточно заменить папку с программой.

## Выпуск версии

`.github/workflows/release.yml`: сборка, тесты, смоук-запуск и GitHub Release с zip. Запуск — push тега вида `v1.2.3`
или вручную: Actions → Release → Run workflow, указать версию `1.2.3` (тег создастся сам).

## Сборка

Требуется .NET SDK 10 (см. `global.json`).

```powershell
dotnet build GameLauncher.slnx -c Release
dotnet test --project tests/GameLauncher.Core.Tests
dotnet publish src/GameLauncher.App -c Release -r win-x64 -o artifacts/GameLauncher
```

`GameLauncher.App` собирается только на Windows. `GameLauncher.Core` и тесты собираются на любой ОС.

## Структура

- `src/GameLauncher.Core` — логика без зависимостей от Windows (модели, хранилище, учёт времени, SteamGridDB).
- `src/GameLauncher.App` — UI на WinUI 3 + CommunityToolkit.Mvvm, Windows-реализации интерфейсов из Core.
- `tests/GameLauncher.Core.Tests` — тесты xUnit v3.
