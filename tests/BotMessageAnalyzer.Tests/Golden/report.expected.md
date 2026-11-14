# Bot message analysis

## Run information

| | |
|---|---|
| Input file | sample.json |
| Conversations | 9 |
| Turns | 10 |
| Model | test-model |
| Prompt versions | classify.v1, merge-topics.v1 |
| Date | 2026-01-15 |

## Summary

| Verdict | Turns | Share of all turns |
|---|---|---|
| Answered | 2 | 20.0% |
| NotUnderstood | 5 | 50.0% |
| WrongAnswer | 2 | 20.0% |
| Unclassified | 1 | 10.0% |

## Top gaps

| Rank | Topic | Turns | Share of failed turns | Not understood | Wrong answer | Suggested action |
|---|---|---|---|---|---|---|
| 1 | parking information | 3 | 42.9% | 3 | 0 | Add an intent and training phrases |
| 2 | cancelling an appointment | 2 | 28.6% | 1 | 1 | Add an intent and training phrases; review the bot's answer |
| 3 | billing \| invoices | 1 | 14.3% | 1 | 0 | Add an intent and training phrases |
| 4 | holiday opening hours | 1 | 14.3% | 0 | 1 | Review the bot's answer |

## Gap details

### 1. parking information (3 turns)

- Customer: Is there parking at the clinic?
  - Bot: Sorry, I did not understand that.
- Customer: where can I park
  - Bot: Sorry, I did not understand that.
- Customer: do you have a car park
  - Bot: Sorry, I did not understand that.

Conversations: c-001, c-002, c-003.

### 2. cancelling an appointment (2 turns)

- Customer: I want to cancel
  - Bot: Sure. Which day would you like?
- Customer: no cancel
  - Bot: Can you rephrase that, please?

Conversations: c-005.

### 3. billing | invoices (1 turn)

- Customer: invoice for October
  - Bot: (no reply)

Conversations: c-009.

### 4. holiday opening hours (1 turn)

- Customer: Are you open on Christmas Eve?
  - Bot: We are open Monday to Friday 8:00 to 18:00.

Conversations: c-004.

## Limits

- Every verdict and topic comes from a language model plus a fixed list of fallback phrases. Spot-check the results against the original logs before acting on them.
- Unclassified turns: 1. The model call failed for these turns, so they are not counted in any gap.
- Topic consolidation did not complete. Topics were only lowercased and trimmed, so similar topics may appear as separate gaps.
