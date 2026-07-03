# Rhythm UI System

Документ описывает визуализацию ритма (Rhythm Line) и её реализацию.

Цель:
- сделать ритм интуитивным
- снизить когнитивную нагрузку
- превратить UI в "инструмент", а не просто отображение

---

## Core Concept

Rhythm Line — это таймлайн ритма:

- показывает прошлые удары
- текущий момент
- будущие удары

Игрок читает её как музыкальную партию.

---

## UI Variants

### Variant 1 — Playhead Line (MVP RECOMMENDED)

Линия статична, курсор движется.

Пример:

|  R   L   R   R   R   L   _   R   L  |
        ↑
     PLAYHEAD

---

### Variant 2 — Scrolling Line

Линия движется, курсор фиксирован.

Пример:

R   L   R   R   R   L   _   R   L   →
                ↑
             HIT ZONE

---

### Variant 3 — Hybrid (Future)

- статичная линия
- пульсация элементов
- микро-анимации

---

## Final Decision (MVP)

Использовать:
- Variant 1 (Playhead Line)

Причины:
- проще реализовать
- лучше читается
- легко расширяется

---

## Visual States

Каждая нота имеет состояние:

- Past (сыграно)
- Current (текущий hit)
- Future (будущие)

---

### Цвета

- Past → серый
- Current → белый / яркий
- Future → полупрозрачный

---

### Hit Results

- Perfect → зелёный
- Good → жёлтый
- Miss → красный

---

## Playhead

Playhead — главный элемент UI.

Функция:
- показывает момент удара
- синхронизирован с RhythmManager

---

## Layout

Рекомендуемая структура:

RhythmUI (Control)
├── RhythmLine (HBoxContainer)
│   ├── Note
│   ├── Note
│   ├── Note
├── Playhead (Control)

---

## Data Model

```csharp
public enum HitType
{
    Left,
    Right,
    Pause
}

public enum NoteState
{
    Past,
    Current,
    Future
}

public class RhythmNote
{
    public HitType Type;
    public NoteState State;
    public HitResult Result;
}