using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Pipes;
using System.Net.Sockets;
using System.ComponentModel;
using System.Text;
using System.Threading;

public class ControlScript : MonoBehaviour
{
    public string inputMethod;

    private enum inputMethodE { Glove=0, Box};
    private inputMethodE im;
    private System.Guid g;

    private string namedPipe;
    private NetworkStream stream;
    private TcpClient client;
    private Thread namedPipeThread;
    private Queue gloveGestureQueue;

    private float roll = 0.0F;
    private float pitch = 0.0F;
    private float yaw = 0.0F;


    void Start()
    {
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
        // generate id
        g = System.Guid.NewGuid();
    }

    void Update()
    {
        if (im == inputMethodE.Glove)
        {
            // check if we got something on the named pipe
            if (gloveGestureQueue.Count > 0)
            {
                string gestureString = (string)gloveGestureQueue.Dequeue();
                Debug.Log("Gesture string (out of queue) is " + gestureString);
                // parse gestureString - of form '4 1,1,1'
                string[] splitRes = gestureString.Split(' ');
                System.Int32 gestureCode, xVal, yVal, zVal;
                bool res = System.Int32.TryParse(splitRes[0], out gestureCode);
                string[] xyzStrings = splitRes[1].Split(',');
                Debug.Log("xyzStrings raw is " + xyzStrings[0] + " " + xyzStrings[1] + " " + xyzStrings[2]);
                bool xRes = System.Int32.TryParse(xyzStrings[0], out xVal);
                bool yRes = System.Int32.TryParse(xyzStrings[1], out yVal);
                bool zRes = System.Int32.TryParse(xyzStrings[2], out zVal);
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
                            gameObject.transform.Rotate(xVal, yVal, zVal);
                            break;
                        case 4:
                            // Pan
                            Debug.Log("Gesture received is Pan and we are going to translate by: " + xVal.ToString() + " " + yVal.ToString() + " " + zVal.ToString());
                            gameObject.transform.localScale += new Vector3(xVal, yVal, zVal);
                            break;
                        default:
                            Debug.Log("Gesture code not recognized");
                            break;
                    }
                } else
                {
                    Debug.Log("Error parsing values out of named pipe");
                    return;
                }
            }
        } else if (im == inputMethodE.Box)
        {
            gameObject.transform.rotation = Quaternion.Euler(new Vector3(roll, yaw, pitch));
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
            clientStream.Connect(60);
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
                ret = clientStream.Read(buffer, 0, buffer.Length);
            } catch (System.IO.IOException e)
            {
                Debug.Log("Failed to read from pipe");
                break;
            }
            if (ret > 0)
            {
                Debug.Log("Length of buffer gotten from pipe is " + ret.ToString());
                char[] bufCharArr = new char[ret + 1];
                System.Array.Copy(buffer, 0, bufCharArr, 0, ret);
                bufCharArr[ret] = '\0';
                Debug.Log(bufCharArr);
                string bufString = new string(bufCharArr);
                Debug.Log("buf string is " + bufString + " with length " + bufString.Length);
                // TODO: this is not getting full string (its just giving 4)
                //string bufString = System.Text.Encoding.UTF8.GetString(buffer, 0, ret);
                gloveGestureQueue.Enqueue(bufString);
            }
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
}
