using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProxyReverso.API
{
    [Serializable]
    public class DadosRetornados
    {
        public int StatusCode { get; set; }

        public Dictionary<string, IEnumerable<string>> ResponseHeaders { get; set; } = new Dictionary<string, IEnumerable<string>>();

        public Dictionary<string, IEnumerable<string>> ResponseContentHeaders { get; set; } = new Dictionary<string, IEnumerable<string>>();

        public string DadosBody
        {
            get
            {
                ResetaPosicaoMemoryBody();
                var retorno = new StreamReader(Body).ReadToEnd();
                ResetaPosicaoMemoryBody();
                return retorno;
            }
            set
            {
                var streamWriter = new StreamWriter(Body);
                streamWriter.Write(value);
                streamWriter.Flush();
                ResetaPosicaoMemoryBody();
            }
        }

        private void ResetaPosicaoMemoryBody()
        {
            Body.Position = 0;
        }

        [NonSerialized]
        public MemoryStream Body = new MemoryStream();

    }
}
