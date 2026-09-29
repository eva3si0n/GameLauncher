# Правила репозитория GameLauncher

Отвечать и писать комментарии/документацию — на русском.

## Стек
- WinUI 3 + C#, .NET 10 (LTS). Preview/RC-версии .NET не использовать.
- Windows App SDK 2.x: подключается только компонент `Microsoft.WindowsAppSDK.WinUI` (не метапакет — иначе в сборку попадают AI, Search, Widgets, ML; ~20 МБ). Нужен новый компонент SDK — добавить его пакет отдельно. MVVM — CommunityToolkit.Mvvm.
- Распространение: unpackaged (`WindowsPackageType=None`), self-contained .NET и Windows App SDK, x64. Без MSIX и подписи.
- Локализации: только русский и английский (`SatelliteResourceLanguages=en;ru`, .mui WinUI фильтруются в `GameLauncher.App.csproj`).
- Целевая ОС — только Windows 11.
- Версии NuGet-пакетов — только в `Directory.Packages.props` (central package management).
- Тесты — xUnit v3 на Microsoft.Testing.Platform (`dotnet test --project ...`, раннер задан в `global.json`).

## Структура
- `src/GameLauncher.Core` — `net10.0`, никаких зависимостей от Windows. Всё, что трогает ОС (процессы, DPAPI, иконки), — за интерфейсом; реализация в App.
- `src/GameLauncher.App` — WinUI 3, `net10.0-windows`. ViewModel отвечают за экран и диалоги; логику (обложки — `CoverService`, описания — `DetailsService`, удаление — `GameRemover`) держать в сервисах Core, чтобы она покрывалась тестами на Linux.
- Запись файлов данных — только через `AtomicFile` (Core).
- `tests/GameLauncher.Core.Tests` — тесты Core.
- Данные пользователя — JSON в `%LocalAppData%\GameLauncher`, запись атомарная (временный файл + замена).

## Известные ловушки
- `EnableMsixTooling=true` в `GameLauncher.App.csproj` обязателен и без MSIX: без него в publish не попадает `.pri` приложения, и оно падает при старте (`0xC000027B` в `Microsoft.UI.Xaml.dll`). Не удалять.
- Путь процесса — только через `QueryFullProcessImageName` (`PROCESS_QUERY_LIMITED_INFORMATION`); `Process.MainModule` падает на повышенных/защищённых процессах.
- Steam `appdetails` отвечает `success=false`, если игра не продаётся в регионе `cc`; поэтому витрины перебираются (основная из настроек → us → ru).
- Смоук-запуск в CI (`Smoke launch`) не отключать: только он ловит падения при старте — XAML/ресурсы не проверяются ни компиляцией, ни тестами Core.
- Один экземпляр: своя точка входа `Program.cs` (`DISABLE_XAML_GENERATED_MAIN`, `AppInstance.FindOrRegisterForKey`). Два экземпляра перезаписывали бы друг другу `library.json`. Смоук-тест проверяет, что второй запуск сразу завершается.

## Сборка и проверка
- App собирается только в CI (`windows-latest`, `.github/workflows/ci.yml`): XAML-компилятор WinUI работает только на Windows.
- Локально на Linux: `dotnet build src/GameLauncher.Core` и `dotnet test --project tests/GameLauncher.Core.Tests`.
- Из облачной сессии Claude недоступны SteamGridDB и Steam Store (сетевая политика) — HTTP-клиенты тестируются на подставных ответах (`FakeHttpHandler`), реальную работу проверяет владелец на Windows 11. GUI в контейнере не запустить.
- Артефакт CI — `GameLauncher-win-x64` (zip папки publish), хранится 7 дней — только для проверки сборки PR.
- Релиз — `.github/workflows/release.yml`: push тега `vX.Y.Z` или ручной запуск с версией `X.Y.Z` (тег создаётся сам); zip публикуется в GitHub Releases. Push тегов из облачной сессии Claude не проходит — используйте ручной запуск.

## Порядок работы
- Каждое изменение — отдельный PR в `main`; после зелёного CI владелец проверяет артефакт на Windows 11.
- Цепочку PR (каждый поверх предыдущего) мёржить по порядку обычным merge-коммитом, не squash — иначе следующие PR получат чужие изменения и конфликты. После мёржа — перевести следующий PR на `main`.
- Перед отклонением от согласованного плана — спросить владельца.

## Секреты
- Секреты (API-ключ SteamGridDB и любые другие) никогда не коммитить — ни в код, ни в конфиги, ни в тесты.
- Ключ SteamGridDB вводит пользователь (Настройки → «Изменить API-ключ…»); хранится зашифрованным через DPAPI (CurrentUser) в `%LocalAppData%\GameLauncher\steamgriddb.key`.
