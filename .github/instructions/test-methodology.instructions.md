---
description: 'Test-driven development methodology for C# code and tests'
applyTo: '**/*.cs'
---

# Test Methodology

Use test-driven development for behavior changes, bug fixes, and new code paths. Start with a failing test, implement the minimum code to pass, then refactor with the test suite green.

## Core Loop

Follow the red, green, refactor cycle:

1. Write one small failing test that captures the desired behavior.
2. Run the test and confirm that it fails for the right reason.
3. Implement the smallest change that makes the test pass.
4. Refactor the code and the test while keeping all tests green.
5. Repeat for the next behavior.

Keep the cycle short. If a change grows too large, split it into smaller behaviors and test each one independently.

## What To Test First

Prefer tests that describe externally visible behavior:

* Public methods and command handlers
* Business rules and validation
* Regression cases for bugs that have already occurred
* Boundary conditions and error cases

Start with the simplest behavior that proves the feature exists. Add deeper cases after the basic path is covered.

## Red Phase

During the red phase:

* Write a test that fails before implementation exists
* Make the failure specific and meaningful
* Avoid test setup that hides the real behavior under test
* Tighten the test if it passes unexpectedly
* Notify the user to validate the failing test outcome before continuing to implementation

A test that fails for the wrong reason does not prove the behavior.

## Green Phase

During the green phase:

* Implement only what the failing test requires
* Prefer the smallest correct change over a broad rewrite
* Avoid adding untested features while chasing the first passing test
* Keep the implementation simple enough to understand immediately

If a larger design becomes obvious, note it and come back to it after the test passes.

## Refactor Phase

After the test passes:

* Remove duplication
* Clarify names
* Extract helpers only when they improve readability
* Keep behavior unchanged
* Re-run the full relevant test set after each meaningful refactor

Refactoring is part of TDD, not a separate optional step.

## Check-in and Validation Discipline

After the red phase, notify the user to validate the failing test outcome before continuing.

Check in work as each phase completes, but do not push to remote until the full TDD cycle is complete and the relevant tests are green.

## Test Quality

Write tests that are:

* Deterministic
* Isolated
* Fast
* Easy to read
* Focused on one behavior

Prefer one assertion per test when practical. Related assertions that validate the same behavior are acceptable, but avoid mixing unrelated checks in the same test.

Use clear Arrange, Act, Assert structure. Name tests by expected outcome and scenario so the failure reads like a sentence.

## Regression First

When fixing a bug:

1. Write a regression test that reproduces the bug
2. Confirm the test fails
3. Fix the bug with the smallest safe change
4. Keep the regression test in place

Do not fix a bug only in code. The test is the durable proof that the behavior is corrected.

## Boundaries And Doubles

Use test doubles only when they help isolate the behavior under test.

* Prefer real objects for simple value behavior
* Use substitutes or fakes for external dependencies, I/O, or expensive operations
* Test collaboration at the boundary, not internal implementation details
* Avoid brittle tests that assert private structure instead of observable behavior

## When Not To Overdo It

Use TDD to guide behavior, not to slow down obvious mechanical work.

* Simple wiring and boilerplate may not need a full red-green-refactor cycle
* Keep trivial setup lean
* Do not write broad speculative tests for behavior that is not yet part of the task

If a change is significant enough to affect behavior, it should usually start with a test.

## Practical Rules

* Do not add production code without a failing test unless you are doing a pure refactor or a non-behavioral file change
* Keep tests close to the behavior they protect
* Prefer the smallest useful scope for each test
* Re-run the relevant test suite before moving to the next change

## Related Guidance

Follow the C# test authoring conventions in `csharp-tests.instructions.md` for naming, organization, and mocking patterns.
