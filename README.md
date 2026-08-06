# GetJobCV

GetJobCV is a desktop application that attempts to score your resume against a given job description locally. It achieves this by implementing algorithms introduced as solutions in several white papers.

## Usage

GetJobCV is currently Work-In-Progress. Do not expect the program to be functional.

## Roadmap

- [x] Extract text from PDF
- [x] Preprocess extracted text (lowercasing, stripping symbols and punctuation, tokenizing, and droping stopwords from NTLK)
- [x] Extract GitHub and LinkedIn socials
- [ ] Feature extraction and label encoding
    - [x] Named Entity Recognition
        - [x] Pull O\*NET skills instead of hardcoding skills.txt / bundle O\*skills
    - [x] Count
    - [x] TF-IDF
    - [ ] Semantic
    - [ ] Doc2vec
    - [x] Combine scoring!
- [ ] Refactor code
    - [ ] Add error handling
    - [ ] Add Stemming / Lemmatization
- [ ] Redo UI
    - [ ] Add the ability to run multiple resumes against a job
- [ ] Add exporting the report

## References

Chavan, Prasad & Chandurkar, Yash & Tidake, Ankita & Lavankar, Gaurav & Gaikwad, Suhani & Chavan, Rohit. (2024). Enhancing recruitment efficiency: An advanced Applicant Tracking System (ATS). Industrial Management Advances. 2. 6373. 10.59429/ima.v2i1.6373. 

Pimpalkar, Amit & Lalwani, Aastha & Chaudhari, Roshan & Inshall, Mohd & Dalwani, Mahak & Saluja, Tarandeep. (2023). Job Applications Selection and Identification: Study of Resumes with Natural Language Processing and Machine Learning. 1-5. 10.1109/SCEECS57921.2023.10063010. 

## Copyright

The skill gazetteer is derived from the O\*NET 30.3 Database by USDOL/ETA, used under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). O\*NET is a trademark of USDOL/ETA.