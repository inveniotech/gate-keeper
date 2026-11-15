# NestJS sample

`createApp` starts a real NestJS module and controller with the Gate-Keeper global
interceptor. The controller sets the JSON content type through NestJS's `Header`
decorator, checks consent and logs through pino's Gate-Keeper hook. Decorator
functions are applied directly so this small JavaScript example needs no
TypeScript transpiler. The interceptor protects `/customer/bvn` with a
one-mebibyte serialisation bound and uses NestJS's real RxJS response pipeline.

From the repository root, after the build and media preparation in
[the sample guide](../README.md):

```powershell
node samples/nestjs/app.mjs artifacts/sample-media artifacts/sample-media/public-keys.json
npm test --prefix samples
```

The demonstration prints a redacted pino record and a filtered response. Tests
initialise the real NestJS application, exercise its HTTP adapter in memory and
close the application afterwards. Unknown synthetic subjects are denied and
their responses remain redacted. The fixed application provider does not
implement storage or authentication. No listening port or runtime CLI is used.
