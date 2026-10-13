# Testing rules

1. **Every test run goes through `.\test.ps1`.** Never run a test runner directly.
2. **Extend the harness, don't work around it.** When a test needs to run in a way `.\test.ps1` can't, amend
   the harness to support it, then use that.
3. **E2E runs only when I ask.** Your own checks use the fast suites. When a change looks like it needs
   browser coverage, recommend the narrowest E2E command and stop. Enforced by the E2E opt-in guardrail.
4. **Group tests by what they verify,** not by when they were written. A new case joins the class for its
   capability; a new class only for a genuinely new capability.
5. **Tests drive the real code.** Never fake the thing under test. The only allowed substitute is a forced
   random roll through the rules seam. A mechanic that spans turns needs a full-battle test.
6. **Test names state what they test,** with no `Test` prefix or suffix.
