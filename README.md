# SteamExplorer
Консольное приложение, которое транслирует `id` приложений `Steam` в `названия` и обратно

Умеет 
- переименовывать названия директорий в указанной родительской директории, а также возвращать изменения (в названии к id через дефис добавляется наименование приложения)
- транслировать id в название (если в через Steam API был найден id)
- транслировать название (по подстроке без учёта регистра) в id (если в через Steam API было найдено название)

Если Вам не понравится работа дефолтного API, вы можете создать в папке с приложением файл `params.env` и указать в нём переменную среды `STEAM_APPS_GET_QUERY`, например:
```
STEAM_APPS_GET_QUERY=https://api.steampowered.com/ISteamApps/GetAppList/v0002/?format=json
```
