# Unity Editor Tools

Набор Unity Editor-скриптов (editor-only tooling) для работы со спрайтами, анимациями,
текстурами, префабами и ассетами. Все скрипты находятся под `Editor`-namespace и
не попадают в билд.

## Установка

Скопируй содержимое репозитория в любую папку внутри `Assets/Editor/` твоего Unity-проекта,
например `Assets/Editor/UnityEditorTools/`. `.meta`-файлы включены, чтобы сохранить GUID.

```
git clone git@github.com:brsk420/UnityEditorTools.git Assets/Editor/UnityEditorTools
```

> Тулзы лежат прямо в корне репы — корень становится папкой внутри `Assets/Editor/`.

## Что внутри

Большинство тулов доступны через меню **`_BrskTools`** (в топ-баре и в контекстном меню `Assets/`).

### Sprites
- **Sprite Animation Generator** — генерация анимаций из спрайтов
- **Sprite 9-Slice Setter** (`SpriteSlicer`) — настройка 9-slice borders
- **Sprite Font Creator** — создание sprite-шрифтов
- **Sprite Atlas Viewer** — просмотр содержимого Sprite Atlas
- **Sprite Grayscale Converter** — перевод спрайтов в grayscale
- **Sprite Vertical Cutter** — нарезка спрайтов по вертикали

### Animation
- **Animation Optimizer** — удаление каждого N-го кадра
- **Animation Property Renamer** — переименование property paths в клипах
- **Rename Animation Clips**
- **Bake Sprite Animation** — запекание анимации в спрайты
- **Transform Copier** — копирование Transform-значений
- **Multi Animator Preview** (`Tools/Multi Animator Preview`)

### Textures
- **Downscale Tool** — даунскейл текстур (−5% … −75%)
- **Downscale Preview** (`_Brsk420.Runtime.DownscalePreview`) — компонент на GameObject со `SpriteRenderer`.
  Крутилка `Quality (%)` показывает в эдиторе, как будет выглядеть спрайт ПОСЛЕ Downscale Tool
  (шакалит только визуал, ассет не трогает). 100% = оригинал. Работает и для **анимации
  (sprite sequence)** — следит за `SpriteRenderer` каждый кадр (edit- и play-mode) и шакалит
  каждый новый кадр на лету, кадры кешируются по исходному спрайту. Сам компонент лежит в
  `Assets/_Brsk420/Runtime/` (вне `Editor/`, иначе его нельзя накинуть на объект), вся логика
  под `#if UNITY_EDITOR` — в билд не попадает.


- **Make Texture Divided By Four** — паддинг текстур до кратности 4
- **Remove Black Background** — удаление чёрного фона (exact / threshold)
- **Texture Tools**

### Assets / Prefabs
- **Renamer** / **Prefix Tool** — пакетное переименование
- **Sort To Folders** / **New Folder With Selection** / **Sort To Folder**
- **GUID ↔ Asset Path**
- **Find References In Project** / **Dependencies Hunter** / **Component Reference Analyzator** — поиск зависимостей
- **Find Missing Scripts**
- **Search Materials By Shader**
- **Replace / Revert TagComponents** — в открытом или выбранном префабе
- **Create SameName Parent** / **Color Reset** (`Set Colors to White`)
- **Bundle Cleaner** (`Clean Bundle`) — очистка AssetBundle
- **Delete Every 2**

### Misc
- **Hierarchy Customizer** — кастомизация Hierarchy-окна
- **Fly Camera** — свободная камера в Scene/Play

## Requirements

Unity (editor-only). Зависит от стандартных Unity Editor API; внешних пакетов не требует.

## Лицензия

`ThirdParty/` содержит сторонние утилиты (DependenciesHunter, ComponentReferenceAnalyzator) —
права на них принадлежат их авторам.
