# Direct Method を Job で使う  
Direct Method はデバイスに対し、デバイスからの応答を必要とする同期的な指示を行うための機構である。詳しくは、[Docsの解説](https://docs.microsoft.com/ja-jp/azure/iot-hub/iot-hub-devguide-direct-methods)を読んでいただきたい。  

---
## 基本形
C# の場合、Direct Method Invocation の基本的なパターンは、以下のとおりである。  
```C#
    var method = new CloudToDeviceMethod("MethodName");
    method.SetPayloadJson(payloadJson);
    try
    {
        var result = await serviceClient.InvokeDeviceMethodAsync(deviceId, method);
        // result の Status、GetPayloadJson() で実行結果を取得
    }
    catch (Exception ex)
    {
        // Exception 発生時の処理
    }

```
IoT Edge の場合、Module の Direct Method をコールすることになるが、その場合は、InvokeDeviceMethodAsync の第一引数と第二引数の間にモジュール名を指定する形になる。 
serviceClient は、Microsoft.Azure.Devices NuGet パッケージのServiceClient のインスタンスであり、アクセスには、Service ロールの接続文字列が必要である。 

--- 
## Job での実行 
実ビジネス向けの IoT ソリューションではスケーラビリティが非常に重要である。数十万台、数百万台、数千万台のデバイスが Azure IoT Hub に接続されているようなケース（例えばそれが数十～数百台のオーダーでも）では、個別のデバイスに対して、Direct Method Invocation を行うよりも、デバイスやモジュールの Twins に保持されたプロパティ等を使ってグルーピングを行い、条件に合致するデバイスやモジュールの Direct Method を一括して Invocation する集合操作的な機能が非常に便利である。  
このケースでは、条件に合致するでデバイスやモジュールに対する Direct Method Invocation をスケジュールするため、以下のパターンとなる。  
```C#
    var jobId = Guid.NewGuid().ToString();
    var method = new CloudToDeviceMethod(methodName);
    method.SetPayloadJson(methodPayload);
    var job =  await jobClient.ScheduleDeviceMethodAsync(jobId, 
        $"FROM devices Where devices.tags.user = '{userName}'",
        method, DateTime.Now, 300);
```
jobId は各ジョブの名前であり、上のケースでは GUID を使っているが、別の形式でもよい。ScheduleDeviceMethodAsync の2番目の引数は、Direct Method Invocation の対象となるデバイスやモジュールの条件を指定している。この例では、tags に user というタグを用意し、そこに利用者情報が入っていると仮定し、特定ユーザーのデバイスのみを対象とする場合の条件である。この部分は、deviceId や module 名、Twins の Desired Properties、Reported Properties 等が利用できる。詳しくは[Docsの解説](https://docs.microsoft.com/ja-jp/azure/iot-hub/iot-hub-devguide-query-language) を参考にされたい。  
この形式で Invoke された Direct Method は、キューイングやスケジューリングの状態を経て、実行状態になり、デバイスへの Invocation が行われ、デバイスからの応答受信を持って、完了状態となる。ネットワークやデバイスの状況によっては、失敗するケースもありうる。詳しくは、[Docsの解説](https://docs.microsoft.com/ja-jp/azure/iot-hub/iot-hub-devguide-jobs)を参照してほしい。  
ちなみに、Job は、Direct Method Invocation だけでなく、タグや Twins の Desired Properties の一括更新も可能である。  
スケジューリングされた Job の状態の取得は以下のパターンで行うことができる。  
```C#
    jobResponse = await jobClient.GetJobAsync(jobId);
```
スケジュールしたジョブが完了すると、jobResponse.Statusが、JobStatus.Completed になる。※失敗するケースもあり得るので、そちらの対応も必要  
条件に合致するデバイスやモジュールの Direct Method で Invocation がスケジューリングされているものがいくつあるかという情報は、jobResponse.DeviceJobStatistics に格納されており、その値をチェックすることにより、実際に幾つの Invocation が実際に遂行されているかを確認できる。  
各 Direct Method Invocation の結果の取得は、以下のパターンで行う。  
```C#
    var query = registryManager.CreateQuery(
        $"SELECT * FROM devices.jobs WHERE jobId='{jobId}'");
    while (query.HasMoreResults)
    {
        foreach(var qJob in await query.GetNextAsDeviceJobAsync())
        {
            var outcome = qJob.Outcome;
            var directMethodResponse = outcome.DeviceMethodResponse;
            var dmStatus = directMethodResponse.Status;
            var responsePayload = directMethodResponse.GetPayloadAsJson();
```
registryManager は、ServiceClient と同じ NuGet パッケージに含まれる、RegistryManager のインスタンスである。これを使って、Azure IoT Hub が保持している情報から検索をかけることによって Invocation の結果の取得が可能である。  
RegistryManager を使ってデータを取得するので、実行するには、サービスロールだけでなく、レジストリ読み込みも可能な接続文字列を使わなければならない。  
CreateQuery でのクエリー文は、jobId のみによる条件付けになっているが、deviceId 等を使った条件指定も可能である。  
このパターンで、集合論な操作で Direct Method Invocation の一括スケジューリングをした場合の、各 Direct Method Invocation の結果を全て取得可能である。  

---
## 最後に  
2020/9/15時点の Docs では、ここで説明した Job を使った一括 Direct Method Invocation の個別の結果の取得方法に関する説明が断片的であり、また、インターネット上にもこのトピックを説明するものなかったので、試してみて動作確認を行い、この一文をまとめた。読者の参考になれば幸いである。  
