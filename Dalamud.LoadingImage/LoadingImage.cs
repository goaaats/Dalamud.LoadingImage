using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using Lumina.Text;

namespace Dalamud.LoadingImage
{
   [Sheet( "LoadingImage" )]
    public class LoadingImage : ExcelRow
    {
        
        public SeString Name { get; set; }

        public override void PopulateData( RowParser parser, GameData gameData, Language language )
        {
            base.PopulateData( parser, gameData, language );

            Name = parser.ReadColumn< SeString >( 0 );
        }
    }
}
