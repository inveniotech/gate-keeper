# Django sample

P3-05, issue #42. This local smoke application uses only the existing synthetic
conformance corpus. It redacts a log record, protects a JSON response field and
calls the synchronous consent helper. It is not a published package.

From the repository root, install the Python development requirements and run
`python samples/django/app.py`. A non-zero exit indicates a failed assertion.
