# Evaluation set

Hand-labeled resumes and job descriptions, used by `GoldSetTests` to measure the whole pipeline
(`Analyzer.Analyze`) and fail when it gets worse.

```
dotnet test tests/GetJobCV.Tests --filter Category=Evaluation --logger "console;verbosity=detailed"
```

- `jobs/*.txt`, `resumes/*.txt`: plain text, laid out the way PdfPig extracts a PDF resume. All people and companies are made up.
- `labels.json`: the skills each document names, and a fit label for each resume/job pair.

## Labeling rules

Label from the text, never from what the pipeline found.

**Skills.** List every skill the document names: languages, tools, platforms, methods, certifications
(BLS, CMA), domain skills (phlebotomy), and soft skills the text states (communication, mentoring,
code review). Use the gazetteer's canonical name when it has one. An alias works too, since both sides
go through the alias map, but `Labels_reference_every_document_and_only_known_skills` lists the names
the gazetteer doesn't know, so check that list for typos. A name that isn't in the gazetteer is still a
valid label. It records a gap the gazetteer should fill.

- Label only what's written. PostgreSQL doesn't imply a SQL label. The matcher's hierarchy handles that, and this set measures extraction.
- Degree and school names aren't skills ("B.S. Mathematics"). Neither are job titles, company names, or plain verbs ("analyze", "writing").
- One label per concept: "Azure Kubernetes Service" and "Kubernetes" are both named, so both are labeled.

**Fit.** Label each pair the way a recruiter screening for this one job would:

- `good`: meets the minimum qualifications; would get a phone screen.
- `potential`: related background, with a real gap in years or in a core skill; worth a second look.
- `no`: a different field, or missing most of the minimum qualifications.

Each job should have at least one pair of each fit, so the within-job ranking metrics have something to compare.

## Metrics

- **Skill extraction:** micro-averaged precision, recall, and F1 of the skills NER finds in each document.
- **Within-job concordance:** of every two resumes for the same job with different fits, the share where the better fit scored higher. The main ranking number.
- **Good vs No AUC:** the chance a good fit scores above a no fit, across all jobs.
- **NDCG per job:** ranking quality with the fit as gain.
- **Verdict matches label:** how often the verdict text agrees with the fit label.

The fit report also shows concordance for cosine alone and coverage alone, to guide the weights in `ScoreCombiner`.

When a change improves a number, raise its floor in `GoldSetTests` so the gain is kept.

## External benchmark

`FitDatasetBenchmark` runs the pipeline on the test split of
[cnamuangtoun/resume-job-description-fit](https://huggingface.co/datasets/cnamuangtoun/resume-job-description-fit)
(1,759 pairs over 71 jobs, labeled No / Potential / Good Fit). The dataset has no license, so it isn't in
the repo. Download `test.csv` and point `GETJOBCV_FIT_CSV` at it:

```
GETJOBCV_FIT_CSV=path/to/test.csv dotnet test tests/GetJobCV.Tests --filter Category=Benchmark --logger "console;verbosity=detailed"
```

Its labels are noisier and its resumes are text scraped with the line breaks lost ("SummaryDiligent
machine operator…"), so read it as a robustness check next to the gold set, not as ground truth.
