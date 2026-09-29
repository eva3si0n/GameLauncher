# GameLauncher

Нативный лаунчер игр для Windows 11 на WinUI 3 (.NET 10).

## Возможности первой версии (в разработке)

- Библиотека игр: ручное добавление `.exe`, переименование, удаление.
- Кнопка «Играть» (запуск с рабочей папкой игры).
- Учёт времени в игре.
- Обложки из SteamGridDB (нужен свой API-ключ), без ключа — иконка из exe.

## Установка

Ставить ничего не нужно: скачайте zip из артефактов CI (вкладка Actions → последний успешный запуск → `GameLauncher-win-x64`),
распакуйте в любую папку и запустите `GameLauncher.exe`. Приложение self-contained, требует Windows 11 x64.

Данные хранятся в `%LocalAppData%\GameLauncher`.

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
