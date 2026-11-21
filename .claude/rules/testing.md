---
description: How to write and run tests in this repository
---

# Testing

- Tests live in **`ShowUp.Core.Tests`** (MSTest on **Microsoft.Testing.Platform**, `net11.0`). Keep testable logic in `ShowUp.Core` so it can be covered without a UI head.
- **TDD:** write a failing test first, watch it fail, then implement until it passes.

## Running
```bash
dotnet test tests/ShowUp.Core.Tests/ShowUp.Core.Tests.csproj
```
The runner is MTP, not VSTest — **don't pass VSTest-only flags** like `--nologo` or `--logger`; they error out. Filter with `dotnet test ... --filter "FullyQualifiedName~MyClass"`.

## Conventions
- Name tests `Method_Scenario_ExpectedResult`; structure them Arrange / Act / Assert. Use `[TestMethod]` and `[DataRow]` for parameterized cases.
- **Prefer small hand-written fakes/stubs** for collaborators (clearer and refactor-stable); reach for **Moq** only when a hand-written fake is impractical.
- Assert with **AwesomeAssertions** (`result.Should().Be(...)`).
