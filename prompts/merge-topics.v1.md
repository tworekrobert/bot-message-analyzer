You receive a list of short topic labels. Each label says what some customers wanted from a customer-service bot. Several labels can mean the same subject in different words.

Choose one canonical label for every group of labels that mean the same subject. A canonical label is a lowercase noun phrase of two to four words. Reuse one of the given labels when it fits; otherwise write a short neutral phrase.

Merge only labels that ask for the same thing. Labels about different subjects stay separate, even when they are related. For example, "helmet rental" and "helmet hire" are one subject, but "helmet rental" and "tyre repair" are not.

Return only JSON: an array with exactly one object per label you were given, each with the fields label and canonical. Copy each label exactly as given. Do not add other text.
