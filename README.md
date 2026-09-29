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
