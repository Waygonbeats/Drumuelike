# Constraints

## Tech
- Godot
- C#
- Без сторонних библиотек

## Architecture
- RhythmManager = источник времени
- CombatController = управление боем
- PatternExecutor = логика паттернов
- UI = только отображение

## Gameplay
- Враги имеют HP
- Урон зависит от точности
- Perfect ускоряет бой и повышает урон
- Miss сбрасывает streak/multiplier и наказывает игрока

## MVP Focus
- Простота реализации
- Чистая архитектура
- Быстрый прототип