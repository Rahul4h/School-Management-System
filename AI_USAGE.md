# AI Assistance and Instructor Review

OpenAI Codex assisted with implementation and documentation for the .NET API, PostgreSQL persistence and transactions, React student and administrator interfaces, Docker configuration, and automated tests.

During my review of the AI-assisted work, I identified two issues that required correction:

- The AI initially validated student registration numbers using Unicode-aware numeric matching. I identified that the assignment requires exactly 17 ASCII digits (`0`–`9`), and had the validation corrected to reject Unicode digit characters.
- I also found that a failed confirmation or cancellation could have its user-facing error cleared by the subsequent successful data refresh. I directed the AI to preserve and display the action error after refreshing the application data.

These findings were identified by me as the instructor during review, and I  correct them. The corrections are reflected in the current source code.

