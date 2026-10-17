# bismarck-backend

## Unit test coverage

The resource service unit tests use xUnit parameterized cases and Moq test doubles
to isolate service behavior from event publishing. Run them with the enforced
line-coverage threshold:

```bash
dotnet test resource/Resource.Service.Tests/Resource.Service.Tests.csproj \
  --configuration Release \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=TestResults/coverage/ \
  /p:Threshold=86 \
  /p:ThresholdType=line \
  /p:Include='[Resource.Service]Resource.Service.Services.*%2c[Resource.Service]Resource.Service.Validators.*%2c[Resource.Service]Resource.Service.Mappings.*' \
  /p:Exclude='[Resource.Service]Resource.Service.Services.KafkaDomainEventPublisher*%2c[Resource.Service]Resource.Service.Services.DomainEventMessage*%2c[Resource.Service]Resource.Service.Services.NullDomainEventPublisher*'
```