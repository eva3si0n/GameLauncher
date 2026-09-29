# Правила репозитория GameLauncher

Отвечать и писать комментарии/документацию — на русском.

## Стек
- WinUI 3 + C#, .NET 10 (LTS). Preview/RC-версии .NET не использовать.
- Windows App SDK 2.x (метапакет `Microsoft.WindowsAppSDK`), MVVM — CommunityToolkit.Mvvm.
- Распространение: unpackaged (`WindowsPackageType=None`), self-contained .NET и Windows App SDK, x64. Без MSIX и подписи.
- Целевая ОС — только Windows 11.
- Версии NuGet-пакетов — только в `Directory.Packages.props` (central package management).
- Тесты — xUnit v3 на Microsoft.Testing.Platform (`dotnet test --project ...`, раннер задан в `global.json`).

## Структура
- `src/GameLauncher.Core` — `net10.0`, никаких зависимостей от Windows. Всё, что трогает ОС (процессы, DPAPI, иконки), — за интерфейсом; реализация в App.
- `src/GameLauncher.App` — WinUI 3, `net10.0-windows`.
- `tests/GameLauncher.Core.Tests` — тесты Core.
- Данные пользователя — JSON в `%LocalAppData%\GameLauncher`, запись атомарная (временный файл + замена).

## Сборка и проверка
- App собирается только в CI (`windows-latest`, `.github/workflows/ci.yml`): XAML-компилятор WinUI работает только на Windows.
- Локально на Linux: `dotnet build src/GameLauncher.Core` и `dotnet test --project tests/GameLauncher.Core.Tests`.
- Артефакт CI — `GameLauncher-win-x64` (zip папки publish), хранится 7 дней: репозиторий приватный, квота хранилища Actions общая на аккаунт.

## Секреты
- Секреты (API-ключ SteamGridDB и любые другие) никогда не коммитить — ни в код, ни в конфиги, ни в тесты.
- Ключ SteamGridDB вводит пользователь в настройках; хранится зашифрованным через DPAPI (CurrentUser).
