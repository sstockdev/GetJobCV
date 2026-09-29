# GetJobCV

GetJobCV is a desktop application that attempts to score your resume against a given job description locally. It achieves this by implementing algorithms introduced as solutions in several white papers.

## Usage

GetJobCV is currently Work-In-Progress. Do not expect the program to be functional. This project is an experiment to see how effective agentic tooling can be when working on small projects.

## Roadmap

The roadmap follows the pipelines in the two referenced papers. Each step is tagged with its source:
**[P]** Pimpalkar et al. (2023), Fig. 3 and Section III steps 1–6; **[C]** Chavan et al. (2024), Figs. 1–2 and Section 6.

### 1. Extract text from files [P step 1]
- [x] Extract text from PDF
- [x] Extract hyperlinks from PDF annotations
- [ ] Extract text from DOCX and RTF ("pdf, documents, images, and rich text formats")
- [ ] OCR for image-only or scanned resumes [P III.A]
- [ ] Use PDF layout (columns, tables, headers/footers) instead of plain reading order [P IV.4, IV.5, IV.7]

### 2. Preprocessing [P step 2, C Fig. 2]
- [x] Lowercase, strip symbols and punctuation, tokenize, drop NLTK stopwords
- [x] Keep tech tokens intact (C++, C#, .NET, Node.js)
- [ ] Stemming / lemmatization ("Data Prepare → Remove word stemming") [C Fig. 2]
- [x] Section segmentation: split the resume into Education, Experience, Skills, Projects, etc. [P IV.6]
- [x] Use sections in scoring: a matched skill counts fully when used (Experience, Projects, ...), 0.75 when mentioned (Education, Summary, ...), 0.5 when only in the Skills list

### 3. Information extraction: unstructured → structured [P III.A, C Section 6]
- [x] Named Entity Recognition: people, organizations, locations (WikiNER)
- [x] Skill gazetteer from O\*NET plus a curated overlay, with demand tiers and the short names people write for O\*NET's "... software" entries ("PLC", "HubSpot")
- [x] Extract GitHub and LinkedIn socials
- [x] Structured resume record: name, contact, education (degree, school, dates), job titles, companies, dates [P III.A]
- [x] Years of experience, overall and per skill ("Skills and Experience showed significantly improved shortlisting") [C Section 6]
- [x] Skill aliases → canonical names (JS → JavaScript, k8s → Kubernetes), taking the O\*NET demand tier of any alias (`alias<TAB>Canonical` lines in `Resources/skills.txt`)
- [x] Skill hierarchy: a specific skill covers a general one the job asks for (PostgreSQL → SQL, GitHub → Git → version control), at its own evidence and years (`Specific > General` lines in `Resources/skills.txt`)
- [ ] Parse the GitHub profile via the public API: repo languages and topics as a second skill source [C Fig. 1]
- [ ] Parse a LinkedIn profile from a user-supplied export (LinkedIn has no public profile API) [C Fig. 1]
- [ ] Merge CV, GitHub, and LinkedIn skill sets into one candidate skill set with its source for each skill [C Fig. 1]

### 4. Feature extraction and label encoding [P step 3]
- [x] Count (bag of words)
- [x] TF-IDF (IDF off for two-document comparison; on once a resume corpus exists)
- [x] Cosine similarity
- [ ] Semantic features: word embeddings (GloVe/word2vec) for synonym-aware matching
- [ ] Doc2vec document vectors
- [ ] Label encoding of categorical fields (degree level, job category) for the classifiers

### 5. Resume classification [P step 4, C Section 6]
- [ ] Get a labeled resume dataset (resume text → job category)
- [ ] KNN job-role classifier on skill set, proficiency, and experience [C Section 6]
- [ ] Compare against Naïve Bayes, Logistic Regression, SVM, Random Forest, and Decision Tree [P step 4, Table 1]
- [ ] Classify the job description into the same categories and flag role mismatches

### 6. Ranking and shortlisting [P step 5, C Figs. 1–2]
- [x] Weighted skill coverage (matched, missing, and extra skills by O\*NET demand tier)
- [x] Years-of-experience requirements from the job description ("3+ years of Python", "5+ years of experience"); matched skills lose credit when the resume shows fewer years
- [x] Use the overall years requirement in the score: counts as one more requirement in skill coverage (weighted like a hot skill), credited by years shown / years required
- [x] Split the job description into minimum and preferred qualifications: nice-to-have skills ("a plus", "Preferred Qualifications") count half (`QualificationSplitter`)
- [x] Alternatives: a list where any one skill counts ("one of the following", "such as", "Python or Java", an inline topic list) is one requirement (`AlternativeGroups`)
- [ ] Staged ranking: score minimum credentials, then preferred credentials, then interview criteria, each stage with vectorization and cosine similarity [P step 5]
- [x] Overall score: weighted blend of cosine match and skill coverage, with a verdict that's held back when the job has too few recognized skills (`ScoreCombiner`)
- [ ] Combine all stage scores and skill coverage into one final score
- [ ] Shortlist threshold: "similarities matched → CVs shortlisted / not shortlisted" [C Fig. 2]
- [ ] Run multiple resumes against one job and rank them [C Fig. 1 "Ranking Algorithm → Short-listed CVs"]
- [ ] Local candidate store so skill sets and scores are kept between runs [C Fig. 1 "Candidate DB"]
- [ ] Optional: TextRank-style resume summary (sentences → vectors → similarity matrix → graph → ranked sentences) [P Fig. 4]

### 7. Performance evaluation [P step 6]
- [x] Hand-labeled test set of resume/job pairs (fit or no fit, expected skills), run through the whole pipeline (`tests/GetJobCV.Tests/Evaluation`)
- [x] Precision, recall, and F1 for skill extraction; within-job concordance, AUC, and NDCG for fit ranking, with floors that fail on a regression
- [x] Opt-in benchmark on the public resume/job fit dataset (not bundled: no license)
- [x] Grow the gold set to 10 jobs and 21 resumes: DevOps, accounting, iOS, sales, electrical, and teaching next to tech and nursing
- [ ] Keep growing it with fields the gazetteer hasn't been tuned on
- [ ] Accuracy for classification and shortlisting, once those exist
- [ ] Unit tests for each module

### 8. Application
- [x] Background processing and error handling
- [x] Redo UI: start, progress, and report screens instead of a debug text dump
- [x] Export the report as a PDF (printed with WebView2, the Edge engine built into Windows)
- [ ] Job-posting scraping as a job-description source ("web scraping") [C Fig. 2]

Out of scope for a local tool: the hosted-ATS features in [C] (interview scheduling, email automation, assessments, cloud deployment).

## References

Chavan, Prasad & Chandurkar, Yash & Tidake, Ankita & Lavankar, Gaurav & Gaikwad, Suhani & Chavan, Rohit. (2024). Enhancing recruitment efficiency: An advanced Applicant Tracking System (ATS). Industrial Management Advances. 2. 6373. 10.59429/ima.v2i1.6373. 

Pimpalkar, Amit & Lalwani, Aastha & Chaudhari, Roshan & Inshall, Mohd & Dalwani, Mahak & Saluja, Tarandeep. (2023). Job Applications Selection and Identification: Study of Resumes with Natural Language Processing and Machine Learning. 1-5. 10.1109/SCEECS57921.2023.10063010. 

## Copyright

The skill gazetteer is derived from the O\*NET 30.3 Database by USDOL/ETA, used under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). O\*NET is a trademark of USDOL/ETA.