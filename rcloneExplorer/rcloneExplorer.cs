﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.Management;
using System.IO;

namespace rcloneExplorer
{
  public partial class rcloneExplorer : Form
  {
    string[] files;
    string consoletxt;
    IniFile iniSettings;
    string remoteCD = "";
    long totalFilesize = 0;
    public static bool loaded = false;
    private System.Windows.Forms.Timer transferTimer;
    List<String[]> downloading = new List<String[]>();
    List<String[]> downloadPID = new List<String[]>();
    List<String[]> uploading = new List<String[]>();
    List<String[]> uploadingPID = new List<String[]>();

    public rcloneExplorer()
    {

      string inipath = Application.ExecutablePath.Remove(Application.ExecutablePath.Length - 4) + ".ini";
      if (System.IO.File.Exists(inipath))
      {
        //config found, checking for settings
        iniSettings = new IniFile();
 
        if (string.IsNullOrEmpty(iniSettings.Read("rcloneRemote")))
        {
          MessageBox.Show("Incorrect config\r\n\r\nPlease add an rclone remote Name to the config ini");
          Process.Start("cmd.exe", "/c rclone config");
          Process.Start("notepad.exe", inipath);
          Environment.Exit(0);
        }
        else
        {
          //config seems ok so read config settings
          iniSettings.Read("rcloneRemote");
        }
      }
      else
      {
        //file not found!
        iniSettings = new IniFile();
        iniSettings.Write("rcloneRemote", "");
        iniSettings.Write("rcloneVerbose", "false");
        iniSettings.Write("refreshAfterUpload", "false");
        MessageBox.Show("No ini file found!\r\n\r\nPlease add an rclone remote Name to the config ini");
        Process.Start("cmd.exe", "/c rclone config");
        Process.Start("notepad.exe", inipath);
        Environment.Exit(0);
      }

      //check local dir for rclone
      if (!System.IO.File.Exists(AppDomain.CurrentDomain.BaseDirectory + "\\rclone.exe"))
      {
        //rclone not found, quit
        MessageBox.Show(AppDomain.CurrentDomain.BaseDirectory + "\\rclone.exe \r\nfile not found!");
        Environment.Exit(0);
      }

      //start the splashscreen in a background thread so the main form can work away
      new Thread(() =>
      {
        Application.Run(new rcloneSplash());
      }).Start();


      //hide the main window and do some minor UI adjustments
      this.Visible = false;
      InitializeComponent();
      lstExplorer.Columns[0].Width = 0;
      lstExplorer.Columns[2].Width = -2;
      lstExplorer.Columns[3].Width = -2;
      lstDownloads.Columns[1].Width = -2;
      lstUploads.Columns[1].Width = 30;
      lstUploads.Columns[1].Width = -2;
      //run rclone for the first time to get a list of files
      rcloneInit();
    }

    private void rcloneInit()
    {
      //populate the listview with results
      populatelstExplorer(internalExec("lsl", iniSettings.Read("rcloneRemote") + ":"));
      //set console text
      txtRawOut.Text = consoletxt;
      //show total filesize in footer
      lblFooter.Text = "Total Filesize:" + BytesToString(totalFilesize).ToString();
      //mark as loaded/ready (close splashscreen)
      loaded = true;
      //show window
      this.Visible = true;
      //create a timer which can monitor progress periodically
      transferTimer = new System.Windows.Forms.Timer();
      transferTimer.Tick += new EventHandler(transferTimer_Tick);
      transferTimer.Interval = 1000;
      //start timer
      transferTimer.Start();
    }

    private string internalExec(string command, string arguments, string direction = null)
    {

      string rcloneLogs = "";
      //check for verbose logging
      if (iniSettings.Read("rcloneVerbose")=="true")
      {
        rcloneLogs = " --log-file rclone.log --verbose";
      }

      //set up cmd to call rclone
      Process process = new Process();
      process.StartInfo.FileName = "cmd.exe";
      process.StartInfo.Arguments = "/c rclone.exe " + command + " " + arguments + rcloneLogs;
      process.StartInfo.CreateNoWindow = true;
      process.StartInfo.UseShellExecute = false;
      process.StartInfo.RedirectStandardError = true;
      process.StartInfo.RedirectStandardOutput = true;
      process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
      process.StartInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
      process.Start();

      //log process ID
      if (!String.IsNullOrEmpty(direction))
      {
        if (direction == "up")
        {
          uploadingPID.Add(new string[] { process.Id.ToString(), arguments });
        }
        else if (direction == "down")
        {
          downloadPID.Add(new string[] { process.Id.ToString(), arguments });
        }
       
      }
      
      // Synchronously read the standard output of the spawned process. 
      string output = process.StandardOutput.ReadToEnd();
      if (output == null) { output = process.StandardError.ReadToEnd(); }

      //close process when it's finished
      process.WaitForExit();
      process.Close();

      //set raw output
      consoletxt = output.Replace("\n", Environment.NewLine);

      //return output
      return output;
    }

    private void populatelstExplorer(string fileArray)
    {
      //clear existing
      lstExplorer.Items.Clear();
      //clear total
      totalFilesize = 0;
      //add up [..]
      lstExplorer.Items.Add(new ListViewItem(new string[] { "0", "<up>", "", ".." }));
      //breakdown output
      files = fileArray.Split('\n');
      //remove last value which is always null
      files = files.Take(files.Count() - 1).ToArray();
      //create array which will store any directories
      List<string> fileDirList = new List<String>();

      //process to list view
      foreach (string item in files)
      {
        //split entry into filesize and path
        List<string> temp = item.TrimStart().Split(new string[] { " " }, 4, StringSplitOptions.None).ToList();

        //organize stored/remote information
        string fileBytes = temp[0];
        string fileHuman = BytesToString(Convert.ToInt64(temp[0]));
        string fileDate = temp[1];
        string filetime = temp[2].Remove(temp[2].Length - 10);
        string filePath = temp[3];


        if (filePath.Contains("/"))
        {
          string thedir = filePath.Split('/').ToList()[0];
          if (!fileDirList.Contains(thedir))
          {
            //note that this dir is saved
            fileDirList.Add(thedir);
            //create array
            string[] temprow = new string[] { "0", "<dir>", fileDate + " " + filetime, thedir };
            //insert
            lstExplorer.Items.Add(new ListViewItem(temprow));
          }
          else
          {
            //this dir is already covered, do nothing
          }
        }
        else
        {
          //this is a file not a dir, make array
          string[] temprow = new string[] { fileBytes, fileHuman, fileDate + " " + filetime, filePath };
          //insert
          lstExplorer.Items.Add(new ListViewItem(temprow));
        }

        //keep count of total filesize
        totalFilesize += Convert.ToInt64(fileBytes);
        
      }
    }

    private void lstExplorer_MouseDoubleClick(object sender, MouseEventArgs e)
    {
      //setup vars for Stored (remote) files
      long storedFilesizeBytes = Convert.ToInt64(lstExplorer.SelectedItems[0].SubItems[0].Text);
      string storedFilesizeHuman = lstExplorer.SelectedItems[0].SubItems[1].Text;
      string storedDatemodified = lstExplorer.SelectedItems[0].SubItems[2].Text;
      string storedFilepath = remoteCD + lstExplorer.SelectedItems[0].SubItems[3].Text;
      string storedFilename = storedFilepath.Split('/').ToList()[storedFilepath.Split('/').GetUpperBound(0)];

      if (storedFilesizeHuman == "<dir>")
      {
        //set new path
        remoteCD = storedFilepath + "/";
        //show loading in list
        lstExplorer.Items.Clear();
        string[] temprow = new string[] { "0", "0", "0", "loading..." };
        //insert
        lstExplorer.Items.Add(new ListViewItem(temprow));
        //populate lstview with new directory contents
        populatelstExplorer(internalExec("lsl", iniSettings.Read("rcloneRemote") + ":\"" + remoteCD + "\""));
      }
      else if (storedFilesizeHuman == "<up>")
      {
        //seperate directories in string, rebuild array without last two, join back to string
        remoteCD = String.Join(" ", remoteCD.Split('/').Take(remoteCD.Split('/').Count() - 2).ToArray());
        //populate lstview with new directory contents
        populatelstExplorer(internalExec("lsl", iniSettings.Read("rcloneRemote") + ":" + remoteCD + "/"));
      }
      else
      {
        MessageBox.Show("Saving file: " + storedFilename);
        //create save dialog
        FolderBrowserDialog savefile = new FolderBrowserDialog();
        //once a folder has been selected
        if (savefile.ShowDialog() == DialogResult.OK)
        {
          //store the path selected via the dialog and filename taken from the selected entry
          string[] storedvsaved = new string[] { storedFilesizeBytes.ToString(), savefile.SelectedPath + "\\" + storedFilename };
          //store the info into the download history list
          downloading.Add(storedvsaved);
          //then add to list view
          lstDownloads.Items.Add(new ListViewItem(storedvsaved));
          //in a new thread start downloading the file
          new Thread(() =>
          {
            internalExec("copy", iniSettings.Read("rcloneRemote") + ":\"" + storedFilepath + "\" \"" + savefile.SelectedPath + "\"", "down");
          }).Start();
          //set output in console
          txtRawOut.Text = consoletxt;
        }
      }
      //set window title
      this.Text = "rcloneExplorer :" + remoteCD;
    }

    static String BytesToString(long byteCount)
    {
      //http://stackoverflow.com/a/4975942

      string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
      if (byteCount == 0)
        return "0" + suf[0];
      long bytes = Math.Abs(byteCount);
      int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
      double num = Math.Round(bytes / Math.Pow(1024, place), 1);
      return (Math.Sign(byteCount) * num).ToString() + suf[place];
    }

    private bool ProcessExists(int id)
    {
      //http://stackoverflow.com/questions/1545270/how-to-determine-if-a-process-id-exists
      return Process.GetProcesses().Any(x => x.Id == id);
    }
    private void lstDownloads_MouseClick(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
      {
        if (lstDownloads.FocusedItem.Bounds.Contains(e.Location) == true)
        {
          ctxtDownloadContext.Show(Cursor.Position);
        }
      }
    }

    private void lstUploads_MouseClick(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
      {
        if (lstUploads.FocusedItem.Bounds.Contains(e.Location) == true)
        {
          ctxtUploadContext.Show(Cursor.Position);
        }
      }
    }

    private static void KillProcessAndChildren(int pid)
    {
      //http://stackoverflow.com/questions/5901679/kill-process-tree-programatically-in-c-sharp/32595027
      ManagementObjectSearcher searcher = new ManagementObjectSearcher
        ("Select * From Win32_Process Where ParentProcessID=" + pid);
      ManagementObjectCollection moc = searcher.Get();
      foreach (ManagementObject mo in moc)
      {
        KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
      }
      try
      {
        Process proc = Process.GetProcessById(pid);
        proc.Kill();
      }
      catch (ArgumentException)
      {
        // Process already exited.
      }
    }

    private void transferTimer_Tick(object sender, EventArgs e)
    {
      if (downloading.Count >= 0)
      {
        for (var i = 0; i < downloading.Count; i++)
        {
          {
            if (lstDownloads.Items[i].SubItems[0].Text != "100%")
            {
              string[] entry = downloading[i];
              //store the filename of the saved file
              string savedFilename = entry[1];
              //set default filesize for saved file
              long savedFilesizeBytes = 0;
              //check the filesize of the saved file so far
              if (System.IO.File.Exists(savedFilename))
              {
                //get file size
                savedFilesizeBytes = new System.IO.FileInfo(savedFilename).Length;
              }
              //store the filesize of the stored file in bytes for comparison
              long storedFilesizeBytes = Convert.ToInt64(entry[0]);
              //calc percentage
              long percentage = (long)((float)savedFilesizeBytes / storedFilesizeBytes * 100);
              //update percentage
              lstDownloads.Items[i].SubItems[0].Text = percentage.ToString() + "%";
            }
          }
        }
        tabDownloads.Text = "Downloads (" + lstDownloads.Items.Count + ")";
      }
      if (uploading.Count >= 0)
      {
        for (var i = 0; i < uploading.Count; i++)
        {
          {
            //store current iteration from list
            string[] entry = uploading[i];
            //entry filename
            string uploadedFilename = entry[1];

            //check downloadPId proces.exists to see if uploadis complete yet
            int PID = Convert.ToInt32(uploadingPID[i][0]);
            if (ProcessExists(PID))
            {
              //upload still in progress
              lstUploads.Items[i].SubItems[0].Text += ".";
              if (lstUploads.Items[i].SubItems[0].Text == "Uploading...") { lstUploads.Items[i].SubItems[0].Text = "Uploading"; }
            }
            else
            {
              if (lstUploads.Items[i].SubItems[0].Text == "Done!")
              {
                //do nothing
              }
              else
              {
                //upload complete (guessing! probs best to validate this)
                lstUploads.Items[i].SubItems[0].Text = "Done!";
                if (iniSettings.Read("refreshAfterUpload")=="true")
                {
                  refreshlstExplorer();
                }
              }  
            }
          }
        }
        tabUploads.Text = "Uploads (" + lstUploads.Items.Count + ")";
      }
    }

    private void menuStripToggleConsole_Click(object sender, EventArgs e)
    {
      //if collapsed then expand
      if (txtRawOut.Height == 0)
      {
        txtRawOut.Height = 60;
        tabMainUI.Height -= 60;
      }
      else
      //if expanded then collapse
      {
        txtRawOut.Height = 0;
        tabMainUI.Height += 60;
      }
    }

    private void ctxtDownloadContext_Cancel_Click(object sender, EventArgs e)
    {
      //find PID for current transfer (list item order should match with downloadPID list... :( )
      int PID = Convert.ToInt32(downloadPID[lstDownloads.SelectedItems[0].Index][0]);
      //find filename for current transfer (easy enough to pick it from the list since it's selected)
      string FN = lstDownloads.SelectedItems[0].SubItems[1].Text;
      //get progress of file (cant cancel 100%)
      string FP = lstDownloads.SelectedItems[0].SubItems[0].Text;

      //if the file process is 100%, it's done
      if (FP == "100%")
      {
        MessageBox.Show("ERR: Can't cancel a transferred file!");
      }
      //if it's not 100%, it might still be ongoing, so check the process is no longer active
      else if(!ProcessExists(PID))
      {
        MessageBox.Show("ERR: Transfer already completed");
      }
      //file is not 100% and the process is still active
      else
      {
        //kill PID
        KillProcessAndChildren(PID);
        //if the file exists, delete it
        if (System.IO.File.Exists(FN))
        {
          System.IO.File.Delete(FN);
        }
        //mark list entry as cancelled
        lstDownloads.SelectedItems[0].SubItems[1].Text = "Cancelled:" + lstDownloads.SelectedItems[0].SubItems[1].Text;
     }
    }
    private void cancelToolStripMenuItem_Click(object sender, EventArgs e)
    {
      //find PID for current transfer (list item order should match with downloadPID list... :( )
      int PID = Convert.ToInt32(uploadingPID[lstUploads.SelectedItems[0].Index][0]);
      //find filename for current transfer (easy enough to pick it from the list since it's selected)
      string FN = lstUploads.SelectedItems[0].SubItems[1].Text;
      //get progress of file (cant cancel 100%)
      string FP = lstUploads.SelectedItems[0].SubItems[0].Text;

      //if the file process is 100%, it's done
      if (FP == "Done!")
      {
        MessageBox.Show("ERR: Can't cancel a transferred file!");
      }
      //if it's not 100%, it might still be ongoing, so check the process is no longer active
      else if (!ProcessExists(PID))
      {
        MessageBox.Show("ERR: Transfer already completed");
      }
      //file is not 100% and the process is still active
      else
      {
        //kill PID
        KillProcessAndChildren(PID);
        //mark list entry as cancelled
        lstUploads.SelectedItems[0].SubItems[1].Text = "Cancelled:" + lstUploads.SelectedItems[0].SubItems[1].Text;
      }
    }

    private void quitToolStripMenuItem_Click(object sender, EventArgs e)
    {
      //quit app, no worries
      Environment.Exit(0);
    }

    private void quitKillTransfersToolStripMenuItem_Click(object sender, EventArgs e)
    {
      //go through every rclone download process on record
      foreach (string[] entry in downloadPID)
      {
        //get process ID
        int PID = Convert.ToInt32(entry[0]);
        //check if the process is still active
        if (ProcessExists(PID))
        {
          //kill PID
          KillProcessAndChildren(PID);
        }
      }
      //close app
      Environment.Exit(0);
    }

    private void lstExplorer_DragDrop(object sender, DragEventArgs e)
    {
      string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
      foreach (string file in files)
      {
        // get the file attributes for file or directory
        FileAttributes attr = File.GetAttributes(file);

        //detect whether its a directory or file
        if (attr.HasFlag(FileAttributes.Directory))
        {
          //its a folder
          MessageBox.Show("Uploading directory: " + file);
          //in a new thread start uploading the file
          new Thread(() =>
          {
            //get directory name so it can be sent to rclone process
            string dirName = new DirectoryInfo(file).Name;
            //copy local path to rclone folder + directory name
            internalExec("copy", "\"" + file + "\" " + iniSettings.Read("rcloneRemote") + ":\"" + remoteCD + "\\" + dirName + "\"");
          }).Start();
        }
        else
        {
          //its a file
          MessageBox.Show("Uploading file: " + file);
          //in a new thread start uploading the file
          new Thread(() =>
          {
            //copy local path to rclone folder
            internalExec("copy", "\"" + file + "\" " + iniSettings.Read("rcloneRemote") + ":\"" + remoteCD + "\"", "up");
          }).Start();
        }

        //store the path selected via the dialog and filename taken from the selected entry
        string[] temp = new string[] { "Uploading", file };
        //store the info into the download history list
        uploading.Add(temp);
        //add tolistview
        lstUploads.Items.Add(new ListViewItem(temp));
      }
    }

    private void lstExplorer_DragEnter(object sender, DragEventArgs e)
    {
      if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
    {
      refreshlstExplorer();
    }
    private void refreshlstExplorer()
    {
      populatelstExplorer(internalExec("lsl", iniSettings.Read("rcloneRemote") + ":" + remoteCD + "/"));
    }

  }
}
