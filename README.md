# ReQuit

**English** · [Русский](#русский)

ReQuit is an **auto shutdown** app for Windows with a beautiful timer. It can fire not only on a timer, but also when CPU, GPU or network load drops below a threshold. Start a render or a download — the app turns the PC off when it is done. Works as an auto-shutdowner or as a timer with an alarm. Minimizes to the tray and shows the remaining time right on the icon.

![ReQuit screenshot](docs/screenshot.jpg)

## Download

**Download and run [ReQuit.exe](https://github.com/dinosmike/ReQuit/releases/tag/ReQuit_1_0) (version 1.0)**

## Features

- **Shutdown timer**
  - Beautiful borderless interface with animated hours / minutes / seconds digits.
  - Set the timer with clicks, dragging or the mouse wheel; reset it with a right click.
  - Windows shuts down with applications forced to close.
  - Optional delay before shutdown: `none / 30 sec / 1 min / 5 min`.
  - During the delay a **full-screen warning window** appears.
  - "Alarm" mode plays a sound instead of shutting down.
  - If the timer is running, closing the app asks for confirmation.

- **Resource monitor (a condition instead of a timer)**
  - Fires **when load drops below a threshold** (in percent).
  - Resource to watch: `CPU, GPU, Internet`.
  - Protection from false triggers: the condition must hold for **30 seconds** first.
  - Option: **save a screenshot** so you can see what was on the screen before shutdown.

- **Exact time**
  - Set an **exact clock time**, and ReQuit calculates the timer for you.

- **Tray**
  - Minimize to the tray with a title bar button.
  - The tray icon turns into "timer digits" showing the remaining time.

- **Languages**
  - Built in: **Russian / English**.
  - Switch from the interface; the language is remembered.

## How to use

### Simple timer

1. Set `hours : minutes : seconds`.
2. Choose **"Shutdown"** or **"Alarm"**.
3. For **Shutdown** you can choose a **delay** before shutting down.
4. Press **Start**.

### Exact time and resource monitor

- Open with the **"Menu"** button in the title bar, or right-click the background.

## Settings location

ReQuit keeps its settings in a JSON file: %LocalAppData%\ReQuit\ReQuit_settings.json

## Technologies

- Windows 10/11
- **WPF (.NET 6, `net6.0-windows`)**
- NAudio (plays the built-in MP3)
- Performance Counters / System.Management / DXGI (resource monitor: CPU/GPU/network)

---

## Русский

ReQuit — приложение для **автовыключения ПК** с красивым таймером. Умеет срабатывать не только по таймеру, но и если нагрузка на процессор/видеокарту/интернет упадёт ниже заданного порога. Поставил рендер или скачивание чего-либо — программа сама выключит ПК по завершении. Может работать как auto-shutdowner, а может как таймер с сигналом. Умеет сворачиваться в трей и показывать оставшееся время прямо на иконке.

## Запуск

**Скачать и запустить [ReQuit.exe](https://github.com/dinosmike/ReQuit/releases/tag/ReQuit_1_0) (версия 1.0)**

## Возможности

- **Таймер выключения**
  - Красивый безрамочный интерфейс с анимированными цифрами часы/минуты/секунды.
  - Заводить таймер можно кликами, перетаскиванием, скроллом или сбросить правой кнопкой мыши.
  - Выключение Windows происходит с принудительным закрытием приложений.
  - Для выключения можно настроить отсрочку: `нет / 30 сек / 1 мин / 5 мин`.
  - Во время отсрочки появляется **полноэкранное окно-предупреждение**.
  - Режим “Сигнал” вместо выключения воспроизводит аудио-оповещение.
  - Если таймер активен - при закрытии показывается подтверждение.

- **Монитор ресурсов (условие вместо таймера)**
  - Запуск действия **когда нагрузка падает ниже порога** (в процентах).
  - Варианты ресурса для установки порога: `Процессор, Видеокарта, Интернет`.
  - Защита от случайного срабатывания: условие должно выполняться **30 секунд**, прежде чем сработает.
  - Опция: **сохранить скриншот экрана** позволяет увидеть, что было на экране перед выключением.

- **Точное время**
  - Можно указать **точное время на часах**, и ReQuit сам рассчитает таймер до указанного времени.

- **Трей**
  - Сворачивание в трей по кнопке в заголовке.
  - Иконка в трее превращается в “таймер-цифры”, который показывает оставшееся время.

- **Локализация**
  - Встроенные языки: **Русский / English**.
  - Переключение из интерфейса, язык запоминается.

## Как пользоваться

### Обычный таймер

1. Выставьте `часы : минуты : секунды`.
2. Выберите действие **“Выключение”** или **“Сигнал”**.
3. В случае **Выключения** можно выбрать **отсрочку** перед выключением.
4. Нажмите **Старт**.

### Точное время и монитор ресурсов

- Открывается кнопкой **“Меню”** в заголовке окна, либо правым кликом на фоне.

## Настройки и где они лежат

ReQuit хранит настройки в JSON-файле: %LocalAppData%\ReQuit\ReQuit_settings.json

## Технологии

- Windows 10/11
- **WPF (.NET 6, `net6.0-windows`)**
- NAudio (проигрывание встроенного MP3)
- Performance Counters / System.Management / DXGI (монитор ресурсов: CPU/GPU/сеть)
