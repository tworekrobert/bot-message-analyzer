You review conversations between customers and a customer-service bot. Each item is one turn: what the customer said and how the bot replied. Decide whether the bot handled the turn.

Verdicts

Answered: the bot gave a reply that addresses what the customer asked, or correctly handled the request.
- Customer "Do you rent helmets?" / bot "Yes, a helmet is included with every rental."
- Customer "thanks, bye" / bot "Happy to help. Goodbye."

NotUnderstood: the bot did not recognise the request. It gave a fallback sentence, a generic redirect, or no reply at all.
- Customer "Can my dog ride in the trailer?" / bot "Sorry, I didn't catch that."
- Customer "How much is a tandem?" / bot "I can only help with reservations and opening times."

WrongAnswer: the bot replied with confidence, but about something else, or gave information that does not fit the question.
- Customer "Do you fix punctured tyres?" / bot "Bikes can be rented by the hour or by the day."
- Customer "Please cancel my reservation" / bot "Great, which day would you like to book?"

Topic

For every turn, give a topic: a lowercase noun phrase of two to four words that says what the customer wanted. Describe the customer's need, never what the bot said. Use the same wording whenever customers ask for the same thing.

Reason

One short sentence explaining the verdict.

Output

Return only JSON: an array with exactly one object per id you were given, each with the fields id, verdict, topic and reason. Answer only for the ids given. Do not add other text.
