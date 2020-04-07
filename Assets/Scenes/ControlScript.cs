using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Net.Sockets;
using System.ComponentModel;
using System.Text;
using System.Threading;

public class ControlScript : MonoBehaviour
{
    public string inputMethod;
    public GameObject[] availableGameObjects;

    private enum inputMethodE { Glove=0, Box};
    private inputMethodE im;
    private System.Guid g;

    private string namedPipe;
    private NetworkStream stream;
    private TcpClient client;
    private Thread namedPipeThread;
    private Queue gloveGestureQueue;
    private static readonly Object lockobj = new Object();

    private float roll = 0.0F;
    private float pitch = 0.0F;
    private float yaw = 0.0F;

    private GameObject currentGameObject;
    private int gameObjectIndex;

    // for "lerping" and "slerping" our gestures to make them look smooth for the glove
    private bool inRotate;
    private bool inPan;
    private Quaternion startRotation;
    private Quaternion endRotation;
    private float panLength; // for translate
    private Vector3 startPosition; // for translate
    private Vector3 endPosition; // for translate
    private float startTimePan; // for translate
    private float timeCount = 0.0f;

    void Start()
    {
        // initialize object we are controlling
        gameObjectIndex = 0;
        currentGameObject = availableGameObjects[0];
        Debug.Log("Current Game Object set to " + currentGameObject.name);
        // generate id
        g = System.Guid.NewGuid();

        if (inputMethod == "Box")
        {
            im = inputMethodE.Box;
            // callbacks will handle the rest
            
        } else if (inputMethod == "Glove")
        {
            im = inputMethodE.Glove;
            ConnectToGloveDriver();
            // if here, success - now we have a named pipe to read gestures from
            gloveGestureQueue = new Queue();
            namedPipeThread = new Thread(new ThreadStart(readNamedPipe));
            namedPipeThread.Start();
        } else
        {
            exitCurrentRuntime();
        }
       
    }

    void Update()
    {
        // handle key presses
        if (Input.GetKeyUp(KeyCode.UpArrow))
        {
            if (gameObjectIndex + 1 >= availableGameObjects.Length)
                gameObjectIndex = 0;
            else
                gameObjectIndex += 1;

            currentGameObject = availableGameObjects[gameObjectIndex];
            Debug.Log("Current Game Object set to " + currentGameObject.name);
        }
        if (Input.GetKeyUp(KeyCode.DownArrow))
        {
            if (gameObjectIndex == 0)
                gameObjectIndex = availableGameObjects.Length - 1;
            else
                gameObjectIndex -= 1;

            currentGameObject = availableGameObjects[gameObjectIndex];
            Debug.Log("Current Game Object set to " + currentGameObject.name);
        }

        // handle events from input methods
        if (im == inputMethodE.Glove)
        {
            float translateSpeed = 45f;
            int rotateSpeed = 25;

            // check if we are in motion first
            if (inRotate)
            {
                Debug.Log("In rotate");
                float rotateCovered = (Time.time - startTimePan) * rotateSpeed;
                float fractionOfJourney = rotateCovered / (Quaternion.Angle(endRotation, startRotation));
                currentGameObject.transform.rotation = Quaternion.Slerp(startRotation, endRotation, fractionOfJourney);

                if (Quaternion.Angle(endRotation, currentGameObject.transform.rotation) < 2)
                {
                    inRotate = false;
                    timeCount = 0.0f;
                }
            }
            else if (inPan)
            {
                Debug.Log("In pan");
                float distCovered = (Time.time - startTimePan) * translateSpeed;
                float fractionOfJourney = distCovered / panLength;
                currentGameObject.transform.position = Vector3.Lerp(startPosition, endPosition, fractionOfJourney);
                if (Vector3.Distance(currentGameObject.transform.position, endPosition) < 3) { 
                    inPan = false;
                }
            }
            else
            {

                // check if we got something on the named pipe
                int sizeQueue;
                lock(lockobj)
                {
                    sizeQueue = gloveGestureQueue.Count;
                }
               // Debug.Log("size queue is");
                //Debug.Log(sizeQueue.ToString());
                if (sizeQueue > 0)
                {
                    byte[] gestureByteArr;
                    lock (lockobj)
                    {
                        gestureByteArr = (byte[])gloveGestureQueue.Dequeue();
                    }
                    // parse byte array - of form '4 0:1,0:1,0:1'
                    // it is a wchar_t* - so we have 2x the number of bytes
                    byte[] gba = removeExtraBytes(gestureByteArr);

                    // now convert it to a string so we can parse
                    string gestureString = System.Text.Encoding.UTF8.GetString(gba);
                    Debug.Log(gestureString);
                    string[] splitRes = gestureString.Split(' ');
                    System.Int32 gestureCode, xVal, yVal, zVal;

                    bool res = System.Int32.TryParse(splitRes[0], out gestureCode);
                    string[] xyzStrings = splitRes[1].Split(',');
                    Debug.Log("xyzStrings raw is " + xyzStrings[0] + " " + xyzStrings[1] + " " + xyzStrings[2]);

                    bool xRes = System.Int32.TryParse(xyzStrings[0].Split(':')[1], out xVal);
                    bool yRes = System.Int32.TryParse(xyzStrings[1].Split(':')[1], out yVal);
                    bool zRes = System.Int32.TryParse(xyzStrings[2].Split(':')[1], out zVal);
                    if (xyzStrings[0].Split(':')[0] == "1") xVal *= -1;
                    if (xyzStrings[1].Split(':')[0] == "1") yVal *= -1;
                    if (xyzStrings[2].Split(':')[0] == "0") zVal *= -1; // this is because the axes are off in Unity vs the accelerometer - X goes positive into the camera, y positive to left, z positive down

                    if (res && xRes && yRes && zRes)
                    {
                        switch (gestureCode)
                        {
                            case 1:
                                // Zoom in
                                break;
                            case 2:
                                // Zoom out
                                break;
                            case 3:
                                // Rotate
                                inRotate = true;
                                startRotation = currentGameObject.transform.rotation;
                                endRotation = Quaternion.Euler(currentGameObject.transform.rotation.eulerAngles + new Vector3(xVal * 20, yVal * 20, zVal * 20));
                                startTimePan = Time.time;
                                break;
                            case 4:
                                // Pan
                                inPan = true;
                                startPosition = currentGameObject.transform.position;
                                endPosition = currentGameObject.transform.position + new Vector3(0, yVal * 25.0f, zVal * 25.0f);
                                panLength = Vector3.Distance(currentGameObject.transform.position, endPosition);
                                startTimePan = Time.time;
                                break;
                            default:
                                Debug.Log("Gesture code not recognized");
                                break;
                        }
                    }
                    else
                    {
                        Debug.Log("Error parsing values out of named pipe");
                        return;
                    }
                }
            }
        }
        else if (im == inputMethodE.Box)
        {
            currentGameObject.transform.rotation = Quaternion.Euler(new Vector3(roll, yaw, pitch));
        }
    }

    void ConnectToGloveDriver()
    {
        try
        {
            // Create connect message
            byte[] data = new byte[18];
            data[0] = 1;
            data[17] = (byte)'\n';
            // put uuid in 1-16
            byte[] uuidBytes = Encoding.UTF8.GetBytes(g.ToString());
            System.Array.Copy(uuidBytes, 16, data, 1, 16);
            string bytesAsString = Encoding.UTF8.GetString(data, 1, data.Length - 2);
            Debug.Log("Message to send is " + bytesAsString);

            // Create a TcpClient.
            // Note, for this client to work you need to have a TcpServer 
            // connected to the same address as specified by the server, port
            // combination.
            System.Int32 port = 10500;
            client = new TcpClient("localhost", port);

            // Get a client stream for reading and writing.
            //  Stream stream = client.GetStream();
            stream = client.GetStream();

            // Send the message to the connected TcpServer. 
            stream.Write(data, 0, data.Length);

            Debug.Log("Sent message");

            // Receive the TcpServer.response.

            // Buffer to store the response bytes.
            byte[] buf = new byte[256];

            // String to store the response ASCII representation.
            string responseData = string.Empty;

            // Read the first batch of the TcpServer response bytes.
            System.Int32 bytes = stream.Read(buf, 0, buf.Length);
            responseData = System.Text.Encoding.ASCII.GetString(buf, 0, bytes);

            Debug.Log("Recieved message " + responseData);

            // Parse response
            if (responseData.Contains("\n"))
            {
                byte responseCode = buf[0];
                byte errorCode = 1;
                if (responseCode == errorCode)
                {
                    exitCurrentRuntime();
                }
                namedPipe = responseData.Substring(1, responseData.Length - 2);
                Debug.Log("Named pipe is " + namedPipe);
            } else
            {
                exitCurrentRuntime();
            }
        }
        catch (System.ArgumentNullException e)
        {
            Debug.Log("ArgumentNullException: " + e);
            exitCurrentRuntime();
        }
        catch (SocketException e)
        {
            Debug.Log("SocketException: " + e);
            exitCurrentRuntime();
        }
    }

    void exitCurrentRuntime()
    {
        // Close everything.
        stream.Close();
        client.Close();

        #if UNITY_EDITOR
                     // Application.Quit() does not work in the editor so
                     // UnityEditor.EditorApplication.isPlaying need to be set to false to end the game
           UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    // Runs in a background worker
    void readNamedPipe()
    {
        NamedPipeClientStream clientStream = null;
        // parse pipe name from namedPipe string
        string namedPipeName = namedPipe.Split(new string[] { "PIPE\\" }, System.StringSplitOptions.None)[1];
        try
        {
            Debug.Log("Trying to connect to pipe name " + namedPipeName);
            clientStream = new NamedPipeClientStream(".", namedPipeName, PipeDirection.In);
            clientStream.Connect();
        } catch (Win32Exception e)
        {
            Debug.Log("Failed to connect to named pipe");
            clientStream.Close();
            exitCurrentRuntime();
        }

        Debug.Log("connected to named pipe");
        byte[] buffer = new byte[1024 * 24];
        System.Int32 ret;
        while (true)
        {
            System.Array.Clear(buffer, 0, buffer.Length);
            ret = 0;
            try
            {
                Debug.Log("Before read");
                ret = clientStream.Read(buffer, 0, buffer.Length);
            } catch (System.Exception e)
            {
                Debug.Log("Failed to read from pipe");
                Debug.Log(e.Message);
                break;
            }
            Debug.Log("got past read");
            Debug.Log(ret.ToString());
            if (ret > 0)
            {
                byte[] gestureByteArr = new byte[ret];
                System.Array.Copy(buffer, 0, gestureByteArr, 0, ret);
                Debug.Log("Beforequeue");
                lock (lockobj)
                {
                    gloveGestureQueue.Enqueue(gestureByteArr);
                }
            }
            Debug.Log("end of loop");
        }
        clientStream.Close();
    }

    // On quit
    void OnApplicationQuit()
    {
        if (stream != null)
            stream.Close();
        if (client != null)
            client.Close();
    }

    // Invoked when a line of data is received from the serial device.
    // Callback from Ardity Package
    void OnMessageArrived(string msg)
    {
        // update roll, pitch and yaw
        string[] data = msg.Split(':');
        if (data.Length > 0 && data[0].StartsWith("Orientation"))
        {
            string[] values = data[1].Split(' ');
            roll = float.Parse(values[3]);  //z
            pitch = float.Parse(values[2]); //y
            yaw = float.Parse(values[1]);   //x
        }
    }

    // Invoked when a connect/disconnect event occurs. The parameter 'success'
    // will be 'true' upon connection, and 'false' upon disconnection or
    // failure to connect.
    // Callback from Ardity Package
    void OnConnectionEvent(bool success)
    {
        Debug.Log("On connection event called with bool " + success.ToString());
    }

    // Since we receive input in the form of wchar_t*, but the first byte for each char is just
    // padding, we use this to remove the padding bytes
    byte[] removeExtraBytes(byte[] arr)
    {
        byte[] newArr = new byte[arr.Length / 2 + 1];
        newArr[arr.Length / 2] = 0;
        for (int i = 0; i < arr.Length; i += 2)
        {
            newArr[i / 2] = arr[i];
        }
        return newArr;
    }
}
