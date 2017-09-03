/*
 * Created by SharpDevelop.
 * User: Bogdan
 * Date: 07.06.2012
 * Time: 20:55
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;


namespace AssemblyReconstructor
{
	/// <summary>
	/// Description of MainForm.
	/// </summary>
	public partial class MainForm : Form
	{
		public MainForm()
		{
			//
			// The InitializeComponent() call is required for Windows Forms designer support.
			//
			InitializeComponent();
			
			//
			// TODO: Add constructor code after the InitializeComponent() call.
			//
		}
		public string DirectoryName = "";
		
		void Button1Click(object sender, EventArgs e)
		{
		label2.ForeColor=Color.Blue;
		label2.Text="";
		OpenFileDialog fdlg = new OpenFileDialog();
		fdlg.Title = "Browse for target assembly";
		fdlg.InitialDirectory = @"c:\";
		if (DirectoryName!="") fdlg.InitialDirectory = DirectoryName;
		fdlg.Filter = "All files (*.exe,*.dll)|*.exe;*.dll";
		fdlg.FilterIndex = 2;
		fdlg.RestoreDirectory = true;
		if(fdlg.ShowDialog() == DialogResult.OK)
		{
		string FileName = fdlg.FileName;
		int lastslash = FileName.LastIndexOf("\\");
		if (lastslash!=-1) DirectoryName = FileName.Remove(lastslash,FileName.Length-lastslash);
        if (DirectoryName.Length==2) DirectoryName=DirectoryName+"\\";
		textBox1.Text = FileName;
		}
		
		}
		
		void TextBox1DragDrop(object sender, DragEventArgs e)
		{
		try
        { 
        Array a = (Array) e.Data.GetData(DataFormats.FileDrop);
        if(a != null)
        {
        string s = a.GetValue(0).ToString();
        int lastoffsetpoint = s.LastIndexOf(".");
        if (lastoffsetpoint != -1)
        {
        string Extension = s.Substring(lastoffsetpoint);
        Extension = Extension.ToLower();
        if (Extension == ".exe"||Extension == ".dll")
        {
        this.Activate();
        textBox1.Text = s;
        int lastslash = s.LastIndexOf("\\");
        if (lastslash!=-1) DirectoryName = s.Remove(lastslash,s.Length-lastslash);
        
        }
        }
        }
		}
        catch
        {
        
		}
		}
		
		void TextBox1DragEnter(object sender, DragEventArgs e)
		{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
        e.Effect = DragDropEffects.Copy;
    	else
    	e.Effect = DragDropEffects.None;
		}
		
		void Button3Click(object sender, EventArgs e)
		{
		Application.Exit();
		}
		
		void Button2Click(object sender, EventArgs e)
		{
		label2.ForeColor=Color.Blue;
		string path = textBox1.Text;
		if (path!=""&&File.Exists(path))
		{
		FileStream input=new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
		BinaryReader reader=new BinaryReader(input);
		Metadata_ReaderWriter.MetadataReader mr = new Metadata_ReaderWriter.MetadataReader();
		bool isok = mr.Intialize(reader);
		if (isok) mr.InitPEWriter(reader);
		reader.Close();
		input.Close();
		
		if (isok)
		{
		string newfilename = Path.GetDirectoryName(path);
		if (!newfilename.EndsWith("\\"))
		newfilename=newfilename+"\\";
        
		newfilename=newfilename+Path.GetFileNameWithoutExtension(path)+
							"_recon"+Path.GetExtension(path);
		mr.WriteToFile(newfilename,mr);
		label2.ForeColor=Color.Blue;
		label2.Text = "File saved on "+newfilename;
		}
		else
		{
		label2.ForeColor=Color.Red;
		label2.Text = "Not a valid assembly!";
		}
		}
		}
	}
}
