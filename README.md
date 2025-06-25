This Project is generated to run local jobs that come up.

Specifically this branch is spun up to run manual record review start.

For Matter Id a0LNw000003yfLBMAY, there are over 100 medical records. When going through the standard flow, the message loses its lock and the whole job is canceled resulting in a dead letter queue. This manual job does all the same actions as record review start without having issues
of losing the message lock. This tool/job should be reserved for large amount of medical files to review. This use case is rare. Should there be an influx of these kinds of reviews then a refactor of Medical Review Start would be in order.


As of now, the MatterId is hard coded in the program. Simply fill out the appropriate app settings and run the application to kick off the job

App Settings:
```
{
  "AppSettings": {
    "PostgresConnectionString": "",
    "SalesforceUri": "",
    "SalesforceClientId": "",
    "SalesforceClientSecret": "",
    "ArcherApiKey": "",
    "BoxClientId": "",
    "BoxClientSecret": "",
    "DocrioBearerToken":  ""
  }
}
```
