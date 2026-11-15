# Express sample

`createApp` registers the real `createExpressMiddleware` hook before the customer
route and configures pino with `createPinoHooks`. The handler calls `isGranted`
and sets the JSON content type before `res.json`. The response hook protects
`/customer/bvn` with a one-mebibyte serialisation bound.

From the repository root, after the build and media preparation in
[the sample guide](../README.md):

```powershell
node samples/express/app.mjs artifacts/sample-media artifacts/sample-media/public-keys.json
npm test --prefix samples
```

The demonstration prints a redacted pino record and a filtered response. The
same application factory can be hosted by Express normally. The demonstration
and tests use an in-memory HTTP socket to run the real router without opening
a listening port. Unknown synthetic subjects are denied and their responses
remain redacted. The application-owned provider does not implement storage or
authentication. Shared installation and request helpers live in `samples/`.
