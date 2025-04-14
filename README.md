# Shelltrac

A scripting language for automating shell operations with first-class parallelism and SSH support; borne of frustrations of fighting quotes in multiple-layers of embedded SSH invocations.

## Features

- **Shell Integration**: Direct execution of shell commands with return values using `let res, ... = sh { ... }` blocks
- **SSH Support**: Remote execution with `ssh "host" { ... }` syntax
- **Parallelism**: Built-in parallel execution with `parallel for`: `let x = parallel for i in (8..10) { return i + 1; } #=> [9, 10, 11]`.  I'm sorry about the potentially confusing overloading of the `return` keyword: meaning "return value x and terminate this loop iteration" inside of a for-*expression* but "return value x and terminate function execution" outside of it.
- **Tasks**: Schedule recurring actions with `task "name" every 1000 => do { ... }`
- **String Interpolation**: `"Values can be interpolated: #{variable}"`
- **Data Structures**: Shelltrac objects are .NET objects under the hood, with their type-appropriate methods bound so that e.g. String has the same methods as in C#.

## Getting Started

```bash
git clone https://github.com/yourusername/shelltrac.git
cd shelltrac
dotnet build
```

## Usage 
```

## License

Do Good Things
