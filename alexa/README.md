# Alexa interaction model

The skill invocation name is `cloud audio`.

Primary command:

```text
Alexa, ask cloud audio to play general quarters
```

`PlaySoundIntent` uses an `AMAZON.SearchQuery` slot named `soundName` so filenames and aliases can be supplied as free-form speech.

## Supported requests

- Launch and help
- Play sound
- Stop and cancel
- Pause
- Resume fallback response until playback-session persistence is implemented
- Alexa fallback intent
- AudioPlayer playback started, stopped, and finished lifecycle events

## Validation boundary

`AlexaRequestValidator` rejects invalid signatures, unsupported envelope versions, missing request types, and timestamps more than 150 seconds from the server clock. Production hosting must perform Amazon certificate-chain and signature verification before constructing an envelope with `SignatureValid = true`.

## Deployment

Import `interaction-model.json` into the Alexa Developer Console JSON editor for the skill locale, build the model, and point the skill endpoint at the host adapter that maps Alexa JSON requests into `AlexaRequestEnvelope` and serializes `AlexaResponseEnvelope`.
