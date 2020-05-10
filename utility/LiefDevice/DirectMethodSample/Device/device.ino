#include <ArduinoJson.h>

#include "AZ3166WiFi.h"
#include "DevKitMQTTClient.h"
#include "RGB_LED.h"

#include "gateway-cert.h"

static bool hasWifi = false;
static bool hasIoTHub = false;
static int delayTime = 2000;

void setup() {
  // put your setup code here, to run once:
  if (WiFi.begin() == WL_CONNECTED)
  {
    hasWifi = true;
    Screen.print(1, "Running...");

    DevKitMQTTClient_SetOption("TrustedCerts", edgeCert);
    if (!DevKitMQTTClient_Init(true))
    {
      hasIoTHub = false;
      return;
    }
    hasIoTHub = true;
    DevKitMQTTClient_SetDeviceTwinCallback(twinCallback);
    DevKitMQTTClient_SetDeviceMethodCallback(directMethodCallback);
    DevKitMQTTClient_SetMessageCallback(messageCallback);
  }
  else
  {
    hasWifi = false;
    Screen.print(1, "No Wi-Fi");
  }
}

void loop() {
  // put your main code here, to run repeatedly:
  if (hasIoTHub && hasWifi)
  {
    char buff[128];

    // replace the following line with your data sent to Azure IoTHub
    snprintf(buff, 128, "{\"topic\":\"iot\"}");
    
    if (DevKitMQTTClient_SendEvent(buff))
    {
      Screen.print(1, "Sending...");
    }
    else
    {
      Screen.print(1, "Failure...");
    }
    DevKitMQTTClient_Check();
    delay(delayTime);
  }
}

void messageCallback(const char* message, int length)
{
  Serial.printf("Message received - %d:%s\n",length, message);
  char* msg = (char*)malloc(length+1);
  strncpy(msg, message, length);
  msg[length] = '\0';
  Screen.print(2, msg);
}

static StaticJsonBuffer<1024> jsonBuffer;
void twinCallback(DEVICE_TWIN_UPDATE_STATE updateState, const unsigned char *payLoad, int length)
{
  Serial.printf("Invoked Twin update - %s\n",payLoad);
  JsonObject& root = jsonBuffer.parseObject(payLoad);
  if (root.containsKey("telemetry-interval-msec"))
  {
    delayTime = root["telemetry-interval-msec"];
    Screen.print(2, "Twin updated...");
    Serial.printf("Updated delaytime ->%d\n",delayTime);
  }
}

static RGB_LED rgbLED;

int directMethodCallback(const char *methodName, const unsigned char *payload, int length, unsigned char **response, int *responseLength)
{
  int result = 500;
  char *responseMessage = "\"method-invocation-requested\"";
  char *responseResult = "\"succeeded\"";
  Serial.printf("invoked - %s(%s)\n", methodName, payload);
  if (strcmp(methodName,"show-text")==0)
  {
    JsonObject& root = jsonBuffer.parseObject(payload);
    const char* msg = root["message"];
    Screen.print(2, msg);
    result = 200;
  }
  else if (strcmp(methodName, "brink-led")==0)
  {
    JsonObject& root = jsonBuffer.parseObject(payload);
    int red = root["color"]["red"];
    int blue = root["color"]["blue"];
    int green = root["color"]["green"];
    int round = root["round"];
    int bright = root["bright"];
    int interval = root["interval"];
    Serial.printf("brink-led %d,%d,%d,%d,%d\n", red, blue, green, round, bright, interval);
    for (int i=0;i<round;i++)
    {
      rgbLED.setColor(red,green,blue);
      delay(bright);
      rgbLED.turnOff();
      delay(interval);
    }
    result = 200;
  }
  else
  {
    Serial.printf("Bad method name - %s\n",methodName);
    responseResult = "\"failed\"";
  }

  *response = (unsigned char*)malloc(strlen(responseMessage)+strlen(responseResult)+3);
  sprintf((char *)*response, "{%s:%s}", responseMessage, responseResult);
  *responseLength = strlen((char*)*response);

  return result;
}