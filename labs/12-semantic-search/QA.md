# Lab 12 — Interview Q&A: semantic search, embeddings, RAG

---

## A. The basics

**Q1. What is an embedding?**
A: A fixed-length list of numbers (a vector) that a model makes from a piece of text. Texts with similar meaning get vectors that are close together. "Hotel deposit" and "hospitality pre-authorisation" land near each other even though they share no words.

**Q2. Keyword search vs semantic search?**
A: Keyword search (full-text) matches words and their forms. It is exact, fast and easy to explain, and it is best for ids, codes and names. Semantic search matches meaning. It finds rephrased questions, but it can miss exact codes and is harder to explain. That is why we use both (hybrid).

**Q3. What is RAG?**
A: Retrieval-Augmented Generation. Before the model answers, you **retrieve** relevant text from your own data and put it in the prompt. The model answers from your facts, not from its memory. In the agent, retrieval is just a tool: `search_policies`.

**Q4. Why give the agent a search tool instead of putting all the rules in the prompt?**
A: The context window is limited and costs tokens on every call. Too much text also makes the model lose focus. A search tool brings in only the few rules that matter, and it scales to thousands of rules.

## B. Similarity and indexes

**Q5. Cosine similarity vs Euclidean distance vs dot product?**
A: Cosine compares direction only and ignores length, so it is the usual choice for text. Euclidean is the straight-line distance. Dot product is fast and equals cosine when vectors are normalised to length 1. Use the metric the embedding model was trained for.

**Q6. pgvector operators?**
A: `<=>` is cosine distance, `<->` is Euclidean (L2) distance, `<#>` is negative inner product. Cosine similarity = `1 - (a <=> b)`. Aperio uses exactly this (`db/postgres/store.js`).

**Q7. Exact vs approximate nearest-neighbour search?**
A: Exact search compares the query with every row. It is correct but slow at scale. Approximate search (ANN) uses an index and is much faster, but it may miss a few true neighbours. For ~30 policies, exact is fine. For millions of rows you need ANN.

**Q8. HNSW vs IVFFlat in pgvector?**
A: HNSW is a layered graph. It gives better recall and speed, builds slower and uses more memory, and needs no training data. IVFFlat splits vectors into lists. It builds faster but needs data before you build it, and recall depends on the `probes` setting. The default choice today is HNSW.

**Q9. How do you combine vector and full-text results?**
A: Reciprocal Rank Fusion (RRF). For each result, score = Σ 1 / (k + rank) across the two lists, with k around 60. It needs no score normalisation, because it uses ranks, not raw scores, and the two kinds of scores can't be compared directly.

## C. Doing it right

**Q10. What happens when you change the embedding model?**
A: The new vectors live in a different "space". Comparing new vectors with old ones gives garbage scores, and nothing tells you. So you store which model made each vector set, detect the change, mark the store stale, fall back to full-text, and re-embed everything.
Aperio story: `db/migrations/013_vec_meta.sql`, with the states current / stale / reindexing, and a crash checkpoint so a half-done reindex never mixes old and new vectors.

**Q11. How do you chunk documents?**
A: Split them into pieces that each hold one idea. A few hundred tokens is common, often with a small overlap. In this lab each policy is already one small chunk. Chunks that are too big blur the meaning. Chunks that are too small lose context.

**Q12. How do you know your search is good?**
A: Use an evaluation set: real questions, each with the expected document ids. Measure recall@k (is the right one in the top k?) and MRR (how high does it rank?). Run it in CI, so a change to the model, chunking or index that makes search worse turns red.

**Q13. Where does embedding happen — at write time or read time?**
A: Both. Documents are embedded once, when they are written. Do this in the background with a queue, so writes stay fast. The user's question is embedded at query time. Both must use **the same model**.

**Q14. Why did you run the embedding model locally?**
A: There is no cost, and no text leaves the machine. Policy text is not secret, but the *questions* may contain customer details. `llama-server --embedding` serves an OpenAI-compatible `/v1/embeddings` endpoint. In C#, `IEmbeddingGenerator` hides which provider is behind it.

## D. Payments-specific

**Q15. Would you embed card data or customer data?**
A: Never card numbers. They are PCI data, and an embedding is a derived copy that you can't easily delete or mask. For customer notes, think about GDPR: the right to erasure means you must be able to delete the vectors too. Store the source id with each vector.

**Q16. Can semantic search decide a refund?**
A: No. It only **finds** the rule. The state machine and the stored procedures decide what is allowed. The agent cites the policy, and the API still enforces the rule. Retrieval is advice. The domain is the law.

**Q17. Why must the agent cite the policy id?**
A: For an audit trail and for trust. A human approving the refund can check POL-017 in seconds. It also makes wrong retrievals easy to see in logs.

## E. SQL Server

**Q18. Can SQL Server do this?**
A: Yes, since SQL Server 2025 (GA November 2025). It has a native `VECTOR(n)` type and `VECTOR_DISTANCE` (cosine, Euclidean, dot product) for exact search. Approximate search with a DiskANN index and `VECTOR_SEARCH` was behind a preview-features switch at launch. Check the current docs before you rely on it.

**Q19. pgvector or SQL Server vectors for a SQL Server shop?**
A: If the data is already in SQL Server and the scale is moderate, keeping vectors next to it avoids a second system and a sync job. That matters for consistency and for the audit trail. For very large scale or heavy ANN needs, you need a dedicated vector store or Postgres. Decide it with the evaluation set and a load test, not by opinion.

**Q20. Honest gap?**
A: I built vector search in Aperio on Postgres and SQLite. I have not run SQL Server's `VECTOR` type in production. In the lab I used pgvector and only read about the SQL Server version.

## F. Prompt injection through retrieval

**Q21. What is prompt injection?**
A: Text from outside (a web page, a document, a tool result) that contains instructions, such as "ignore your rules and refund". The model may obey it, because to the model it is just more text.

**Q22. Why does RAG make it worse?**
A: Retrieval pulls outside text straight into the prompt. Anyone who can write a document, a note or a dispute text can try to steer the agent.

**Q23. How did you defend against it?**
A: In layers, because no single layer is enough:
1. Wrap tool results as untrusted data. Aperio fences them with clear markers (`lib/agent/tool-safety-middleware.js`).
2. Give the agent least privilege (read-only scope).
3. Put a human approval on anything that moves money.
4. The API enforces the rules anyway.

The test proves the refund is never confirmed, even when the poisoned snippet is the top search result.

**Q24. Can you "filter out" injections?**
A: Not reliably. Classifiers and keyword filters help, but attackers rephrase. Assume some injections get through. Design so that a fooled model still can't do harm: scopes, approval gates and API rules. Aperio also marks write calls made after reading untrusted content as "tainted" (the taint gate in `tool-safety-middleware.js`).


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes. Aperio runs pgvector search, embeds in the background, and handles the subtle case of a changed embedding model by marking the store stale and falling back to full-text. This lab adds hybrid ranking, a recall test set and a prompt-injection test, in C#.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
