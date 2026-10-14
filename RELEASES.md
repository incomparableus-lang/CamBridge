# Релизы

Установщик не хранится в этом репозитории — он лежит в релизах хостингов.

| Хостинг | Адрес релиза | Ассет |
|---|---|---|
| GitHub | <https://github.com/incomparableus-lang/CamBridge/releases/tag/v1.0.0> | `CamBridge-Setup-1.0.0.exe` |
| GitVerse | <https://gitverse.ru/MEdb/CamBridge/releases/tag/v1.0.0> | `CamBridge-Setup.zip` |

GitVerse не принимает `.exe` как ассет релиза, поэтому там installer отдаётся архивом.
Установщик самораспаковывающийся: распакуйте архив и запустите `CamBridge-Setup.exe`,
двойного распаковывания не будет.

---

## v1.0.0 — 2026-10-14

Первый публичный релиз.

Мост DirectShow → Media Foundation для веб-камер, чей драйвер регистрирует фильтр
захвата только в `KSCATEGORY_CAPTURE`. Физическая камера не изменяется, рядом появляется
виртуальная, видимая в приложении «Камера», Teams, Zoom, браузерах и Discord.
Без ядерного драйвера и без подписи.

Требуется Windows 11 сборки 22000 или новее, x64, права администратора при установке.

Языки интерфейса: русский, белорусский, корейский, персидский (RTL), китайский, хинди.

### Контрольные суммы SHA-256

```
CamBridge-Setup.exe      d5517ba1a825d30918cf10ca6135f690f10740ab5e3be98009ed5fd1abbe1627
CamBridge-Setup.zip      bab6171fbe5679a4200b3f27fb10c8989e71f0d01d6201080c62220cb2ff0fa5
```

`CamBridge-Setup.exe` — то, что отдаёт GitHub; `CamBridge-Setup.zip` — то, что отдаёт
GitVerse. Установщик внутри архива побайтово идентичен файлу на GitHub, поэтому сумма
`.exe` у них общая.

Проверка в PowerShell:

```powershell
Get-FileHash .\CamBridge-Setup.exe -Algorithm SHA256
```

### Что внутри

| Файл | Размер | Назначение |
|---|---|---|
| `CamBridge.exe` | 31 КБ | tray-приложение: настройки, автопоиск камеры и ffmpeg |
| `ffmpeg\ffmpeg.exe` | 100,5 МБ | захват с DirectShow (gyan essentials 9.0.2) |
| `VirtualCamera.msi` | 663 КБ | media source для Media Foundation (LocosLab, MIT) |
| `Uninstall.exe` | 23 КБ | деинсталлятор |

### Известные ограничения

- Установщик не подписан — SmartScreen на чистой машине выдаст предупреждение.
- Камера реально выдаёт ~10 fps при 640x480; мост не дорисовывает кадры.
- В тихом режиме (`/silent`) выбирается первая найденная DirectShow-камера.

### Как собрать самому

Версия `v1.0.0` соответствует тегу `v1.0.0` этого репозитория.

```powershell
git clone https://github.com/incomparableus-lang/CamBridge.git
cd CamBridge
git checkout v1.0.0
.\fetch-deps.ps1
.\build.ps1
```

Собранный `dist\CamBridge-Setup.exe` может отличаться от опубликованного по SHA-256:
компилятор `csc` и упаковщик zip не обязаны давать побайтово воспроизводимый результат.
Сверяйте по поведению, а не по сумме; суммы выше относятся к конкретным опубликованным файлам.
