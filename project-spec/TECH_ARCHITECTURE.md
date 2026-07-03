# Technical Architecture

## Scene Tree
Main
├── AudioPlayer
├── Player
├── Room
│   ├── Enemy
│   ├── Enemy
└── UI
    ├── RhythmLine
    └── ComboMeter

---

## Systems

### RhythmManager
- BPM
- Beat calculation
- Song timing

### InputJudge
- Проверяет точность попадания
- Возвращает Perfect / Good / Miss

### CombatController
- Управляет боем
- Выбор активного врага
- Запуск и завершение паттерна
- Player → CombatController → PatternExecutor → Enemy

### PatternExecutor
- Хранит активный паттерн
- Проверяет последовательность
- Считает урон с учётом Multiplier
- Обрабатывает ошибки (сброс streak на Miss)

### Enemy
- HP
- Pattern
- TakeDamage()
- Counterattack()

### Player
- Движение как в изметрических играх
- Ввод (L / R) пкм лкм
- Dodge  на пробел

---

## Data Structures
- HitType: Left / Right
- HitResult: Perfect / Good / Miss
- RhythmPattern: List<HitType>, Enemy reference

---

## Timing
- beatInterval = 60 / BPM

---

## Damage Logic
- damage = baseDamage * multiplier
- Multiplier увеличивается от streak: multiplier = 1 + (streak / N)
- Streak растёт от непрерывной барабанной партии (Perfect/Good)
- Разрыв streak (Miss) сбрасывает multiplier

---

## Pattern Flow
1. Player input
2. InputJudge → accuracy
3. PatternExecutor → проверка паттерна и урон
4. Enemy → TakeDamage()
5. При ошибке: сброс streak, атака врага

---

## UI
- RhythmLine: отображает последовательность
- Текущий input, результат попадания
- Streak / Multiplier отображается для игрока

---

## Architecture Rules
- RhythmManager = источник времени
- CombatController управляет боем
- PatternExecutor не знает UI
- UI не содержит логики