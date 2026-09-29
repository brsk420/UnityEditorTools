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
Папки ниже совпадают с разделами.

### `Animation/`
- **Animation Optimizer** — удаление каждого N-го кадра
- **Animation Property Renamer** — переименование property paths в клипах
- **Rename Animation Clips**
- **Bake Sprite Animation** — запекание анимации в спрайты
- **Sprite Animation Generator** — генерация анимаций из спрайтов
- **Multi Animation Player** (`Tools/Multi Animation Player`) — одновременное проигрывание клипов на нескольких Animator (edit и play mode)

### `Sprites/`
- **Sprite 9-Slice Setter** (`SpriteSlicer`) — настройка 9-slice borders
- **Sprite Atlas Viewer** — просмотр содержимого Sprite Atlas
- **Repack Folder Into Atlas Groups** (`AtlasPacker/`)
- **Sprite Grayscale Converter** — перевод спрайтов в grayscale
- **Sprite Vertical Cutter** — нарезка спрайтов по вертикали
- **Find Sprite Usages** — где используется спрайт (префабы и анимации)

### `Fonts/`
- **Sprite Font Creator** — создание sprite-шрифтов
- **Bitmap Font Builder** (`BitmapFontBuilder/`) — сборка TMP-шрифта из картинок

### `Textures/`
- **Downscale Tool** (`Downscale/`) — даунскейл текстур (−25% / −50% / −75% / Custom)
- **Downscale Preview** (`_Brsk420.Runtime.DownscalePreview`) — компонент на GameObject со `SpriteRenderer`.
  Крутилка `Quality (%)` показывает в эдиторе, как будет выглядеть спрайт ПОСЛЕ Downscale Tool
  (шакалит только визуал, ассет не трогает). 100% = оригинал. Работает и для **анимации
  (sprite sequence)** — следит за `SpriteRenderer` каждый кадр (edit- и play-mode) и шакалит
  каждый новый кадр на лету, кадры кешируются по исходному спрайту. Сам компонент лежит в
  `Assets/_Brsk420/Runtime/` (вне `Editor/`, иначе его нельзя накинуть на объект), вся логика
  под `#if UNITY_EDITOR` — в билд не попадает.
- **Make Texture Divided By Four** — паддинг текстур до кратности 4
- **Remove Black Background** — удаление чёрного фона (exact / threshold)
- **Open in Photoshop** — пункт в контекстном меню Project

### `Project/`
- **Renamer** / **Prefix Tool** — пакетное переименование
- **Sort To Folders** / **New Folder With Selection**
- **GUID ↔ Asset Path**
- **Search Materials By Shader**
- **Clone Prefab** (`PrefabCloner`)
- **Delete Every 2**

### `Scene/`
- **Create SameName Parent** / **Transform Copier**
- **Replace / Revert TagComponents** — в открытом или выбранном префабе
- **Color Reset** (`CONTEXT/Material/Set Colors to White`)
- **Hierarchy Customizer** — кастомизация Hierarchy-окна
- **Fly Camera** — свободная камера в Scene/Play

### `SlotTools/`
- **Bundle Cleaner V2** (`Assets/_BrskTools/Bundle Cleaner V2/`) — поиск недостижимых ассетов в бандле
- **Scan Nine-Slice Sprites**

### `ThirdParty/`
- **Dependencies Hunter** / **Component Reference Analyzator** — поиск зависимостей
- **FolderColor** — раскраска папок. Хранит пути к своим иконкам в `SaveSetUp/`, поэтому папку не переносить.

## Requirements

Unity (editor-only). Зависит от стандартных Unity Editor API; внешних пакетов не требует.

## Лицензия

`ThirdParty/` содержит сторонние утилиты (DependenciesHunter, ComponentReferenceAnalyzator) —
права на них принадлежат их авторам.
