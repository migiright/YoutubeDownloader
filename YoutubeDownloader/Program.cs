﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YoutubeDownloader
{
	class Program
	{
		private const string ConfigFileName = "YoutubeDownloader.cfg";
		private static Regex QualityParser = new Regex(@"(\d+)p(60)*");

		static void Main(string[] args)
		{
			if(args.Length == 0) {
				Console.WriteLine($"usage: {Environment.GetCommandLineArgs()[0]} url");
				return;
			}

			if (!File.Exists(ConfigFileName)) {
				Console.WriteLine($"{ConfigFileName} not found.");
				return;
			}
			string key;
			using (var sr = new StreamReader(ConfigFileName, Encoding.ASCII)) {
				key = sr.ReadLine();
			}
			Console.WriteLine($"key: {key}");

			string id;
			{
				var match = Regex.Match(args[0], @"^https:\/\/www\.youtube\.com\/channel\/(\w+)\/?.*$");
				if (!match.Success) {
					Console.WriteLine("invalid url.");
					return;
				}
				id = match.Groups[1].Value;
				Console.WriteLine($"id: {id}");
			}

			using(var client = new HttpClient()) {
				string playlistId;
				{
					var result = client.GetAsync(
						"https://www.googleapis.com/youtube/v3/channels"
						+ $"?key={key}&part=contentDetails&id={id}")
						.Result;
					var json = JObject.Parse(result.Content.ReadAsStringAsync().Result);
					playlistId = json.SelectToken("items[0].contentDetails.relatedPlaylists.uploads").ToString();
				}
				Console.WriteLine($"playlistId: {playlistId}");

				string next = null;

				var videoIds = new List<string>();

				while (true) {
					var result = client.GetAsync(
						"https://www.googleapis.com/youtube/v3/playlistItems"
						+ $"?key={key}&part=contentDetails&playlistId={playlistId}&maxResults=50"
						+ (next != null ? $"&pageToken={next}" : ""))
						.Result;
					var json = JObject.Parse(result.Content.ReadAsStringAsync().Result);

					videoIds.AddRange(json.SelectTokens("items[*].contentDetails.videoId")
						.Select(t => t.ToString()));

					if (!json.ContainsKey("nextPageToken")) break;
					next = json["nextPageToken"].ToString();
				};

				Console.WriteLine("videoIds: ");
				foreach (var i in videoIds) {
					Console.WriteLine(i);
				}
				Console.WriteLine();

				foreach (var vi in videoIds) {
					List<(string ext, int quality)> formats;
					{
						var psi = new ProcessStartInfo("youtube-dl.exe") {
							RedirectStandardOutput = true,
							RedirectStandardError = true,
							UseShellExecute = false,
							CreateNoWindow = true,
							Arguments = $"-F https://www.youtube.com/watch?v={vi}",
						};
						string[] output;
						string error;
						using (var p = Process.Start(psi)) {
							output = p.StandardOutput.ReadToEnd()
								.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
							error = p.StandardError.ReadToEnd();
						}

						if (error.Length != 0) {
							Console.WriteLine(error);
							continue;
						}

						var fs = output.SkipWhile(l => l != "format code  extension  resolution note")
							.Skip(1)
							.ToList();

						Console.WriteLine("formats:");
						foreach (var l in fs) {
							Console.WriteLine(l);
						}
						Console.WriteLine();

						formats = fs.Select(l => {
							var ext = l.Substring(13, 11).Trim();
							var note = l.Substring(35);
							var m = QualityParser.Match(note.Substring(0, note.IndexOf(" ")));
							var quality = m.Success ? int.Parse(m.Groups[1].Value) : 0;
							if (m.Success && m.Groups[2].Value == "60") ++quality;
							return (ext, quality);
						}).GroupBy(v => v.quality)
							.OrderByDescending(g => g.Key)
							.First()
							.ToList();
					}

					{
						foreach (var (ext, _) in formats) {
							var audioExt = ext == "mp4" ? "m4a" : ext;
							var psi = new ProcessStartInfo("youtube-dl.exe") {
								RedirectStandardOutput = true,
								RedirectStandardError = true,
								UseShellExecute = false,
								CreateNoWindow = true,
								Arguments = $"-f bestvideo[ext={ext}]+bestaudio[ext={audioExt}]"
									+ $" https://www.youtube.com/watch?v={vi}",
							};
							Console.WriteLine($"-f bestvideo[ext={ext}]+bestaudio[ext={audioExt}]"
									+ $" https://www.youtube.com/watch?v={vi}");
							string output;
							using (var p = Process.Start(psi)) {
								output = p.StandardOutput.ReadToEnd();
							}
							Console.WriteLine(output);
						}
					}
				}
			}
		}
	}
}
