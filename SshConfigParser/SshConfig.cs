using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using static SshConfigParser.Globber;

// based on https://github.com/JeremySkinner/Ssh-Config-Parser

namespace SshConfigParser
{

  public class SshConfig 
  {
    private List<ConfigNode> _nodes = new List<ConfigNode>();
    private static readonly Regex RE_SPACE = new Regex("\\s");
    private static readonly Regex RE_LINE_BREAK = new Regex("\\r|\\n");
    private static readonly Regex RE_SECTION_DIRECTIVE = new Regex("^(Host|Match)$", RegexOptions.IgnoreCase);
    private static readonly Regex RE_QUOTED = new Regex("^(\")(.*)\\1$");

    public SshHost Compute(string host)
    {
      var result = new SshHost();

      void SetProperty(string name, string value)
      {
        //if (!result.Properties.ContainsKey(name))
        {
          result.Properties[name] = value;
        }
      }

      foreach (var line in _nodes)
      {
        if (line.Type != NodeType.Directive)
        {
          continue;
        }

        if (line.Param == "Host")
        {

          if (Glob(line.Value, host))
          {
            SetProperty(line.Param, line.Value);

            line.Config._nodes
                .Where(n => n.Type == NodeType.Directive)
                .ForEach(n => SetProperty(n.Param, n.Value));
          }
        }
        else if (line.Param == "Match")
        {
          // TODO
        }
        else
        {
          SetProperty(line.Param, line.Value);
        }
      }

      return result;
    }

    /// <summary>
    /// Finds a config element by host.
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>

    public IEnumerable<string> FindHosts()
    {

      var query = from line in this._nodes
                  where line.Type == NodeType.Directive
                        && RE_SECTION_DIRECTIVE.IsMatch(line.Param)
                        && line.Param == "Host"
                  select line.Value;


      return query.Where(q => !q.Contains("*") && !q.Contains("?"));
    }

    public static SshConfig ParseFile(string path)
    {
      return Parse(File.ReadAllText(path));
    }
    public static SshConfig Parse(string str)
    {
      var i = 0;
      var chr = Next();
      var config = new SshConfig();
      var configWas = config;

      string Next()
      {
        var j = i++;
        return j < str.Length ? str[j].ToString() : null;
      }

      string Space()
      {
        var spaces = "";

        while (chr != null && RE_SPACE.IsMatch(chr))
        {
          spaces += chr;
          chr = Next();
        }

        return spaces;
      }

      string Linebreak()
      {
        var breaks = "";

        while (chr != null && RE_LINE_BREAK.IsMatch(chr))
        {
          breaks += chr;
          chr = Next();
        }

        return breaks;
      }

      string Option()
      {
        var opt = "";

        while (!string.IsNullOrEmpty(chr) && chr != " " && chr != "=")
        {
          opt += chr;
          chr = Next();
        }

        return opt;
      }

      string Separator()
      {
        var sep = Space();

        if (chr == "=")
        {
          sep += chr;
          chr = Next();
        }

        return sep + Space();
      }

      string Value()
      {
        var val = "";

        while (!string.IsNullOrEmpty(chr) && !RE_LINE_BREAK.IsMatch(chr))
        {
          val += chr;
          chr = Next();
        }

        return val.Trim();
      }

      ConfigNode Comment()
      {
        var type = NodeType.Comment;
        var content = "";

        while (!string.IsNullOrEmpty(chr) && !RE_LINE_BREAK.IsMatch(chr))
        {
          content += chr;
          chr = Next();
        }

        return new ConfigNode { Type = type, Content = content };
      }

      ConfigNode Directive()
      {
        var type = NodeType.Directive;

        return new ConfigNode
        {
          Type = type,
          Param = Option(),
          Separator = Separator(),
          Value = Value()
        };
      }

      ConfigNode Line()
      {
        var before = Space();
        var node = chr == "#" ? Comment() : Directive();
        var after = Linebreak();

        node.Before = before;
        node.After = after;

        if (node.Value != null && RE_QUOTED.IsMatch(node.Value))
        {
          node.Value = RE_QUOTED.Replace(node.Value, "$2");
          node.Quoted = true;
        }

        return node;
      }


      while (chr != null)
      {
        var node = Line();

        if (node.Type == NodeType.Directive && RE_SECTION_DIRECTIVE.IsMatch(node.Param))
        {
          config = configWas;
          config._nodes.Add(node);
          config = node.Config = new SshConfig();
        }
        else
        {
          config._nodes.Add(node);
        }
      }

      return configWas;
    }

    public int Count => _nodes.Count;

    public ConfigNode this[int index] => _nodes[index];

    public IEnumerable<ConfigNode> AsEnumerable() => _nodes;
  }
}
