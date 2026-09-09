using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    public class Helper
    {
        public static List<Attribute> GetAttributeList(IEngineeringObject obj)
        {
            var attributes = new List<Attribute>();

            if (obj != null)
            {
                foreach (var attr in obj.GetAttributeInfos())
                {
                    object value = obj.GetAttribute(attr.Name);

                    attributes.Add(new Attribute
                    {
                        Name = attr.Name,
                        Value = SanitizeAttributeValue(value),
                        AccessMode = Enum.GetName(typeof(EngineeringAttributeAccessMode), attr.AccessMode)
                    });
                }
            }

            return attributes;
        }

        /// <summary>
        /// TIA attribute values are usually primitives, but some (a project's Path as
        /// FileSystemInfo, a description as MultilingualText, ...) are complex Openness/BCL
        /// objects whose Parent/Root/Culture-style navigation properties recurse far past the
        /// JSON serializer's max depth. Reduce anything that isn't safely serializable to a string.
        /// </summary>
        private static object? SanitizeAttributeValue(object? value)
        {
            switch (value)
            {
                case null:
                case string:
                case bool:
                case byte:
                case sbyte:
                case short:
                case ushort:
                case int:
                case uint:
                case long:
                case ulong:
                case float:
                case double:
                case decimal:
                case DateTime:
                case DateTimeOffset:
                case Guid:
                    return value;
                case Enum e:
                    return e.ToString();
                case MultilingualText mlt:
                    return MultilingualTextToString(mlt);
                default:
                    return value.ToString();
            }
        }

        public static BlockGroupInfo BuildBlockHierarchy(PlcBlockGroup group)
        {
            var groupInfo = new BlockGroupInfo
            {
                Name = group.Name
            };

            var blockList = new List<ResponseBlockInfo>();
            foreach (var block in group.Blocks)
            {
                var attributes = Helper.GetAttributeList(block);
                blockList.Add(new ResponseBlockInfo
                {
                    Name = block.Name,
                    TypeName = block.GetType().Name,
                    Namespace = block.Namespace,
                    ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                    MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                    IsConsistent = block.IsConsistent,
                    HeaderName = block.HeaderName,
                    ModifiedDate = block.ModifiedDate,
                    IsKnowHowProtected = block.IsKnowHowProtected,
                    Attributes = attributes,
                    Description = block.ToString()
                });
            }
            groupInfo.Blocks = blockList;

            var groupList = new List<BlockGroupInfo>();
            foreach (var subGroup in group.Groups)
            {
                groupList.Add(BuildBlockHierarchy(subGroup));
            }
            groupInfo.Groups = groupList;

            return groupInfo;
        }
        /// <summary>
        /// Converts a CrossReferenceService result's source tree into the response DTO.
        /// GetCrossReferences on a block/type returns a Source entry for the queried object
        /// *and* one for every element declared/used inside it (local variables, networks, ...) -
        /// only the queried object's own entry (matched by <paramref name="onlyName"/>) answers
        /// "who else uses this block/type"; the rest is internal noise that balloons the response.
        /// <paramref name="maxDepth"/> caps recursion into each source's own internal contents
        /// (Children) for the same reason - defaults to 0 (no Children expansion).
        /// </summary>
        public static List<ResponseCrossReferenceSource> BuildCrossReferenceSourceList(SourceObjectComposition? sources, string? onlyName = null, int maxDepth = 0)
        {
            var list = new List<ResponseCrossReferenceSource>();

            if (sources == null)
            {
                return list;
            }

            foreach (var source in sources)
            {
                if (source == null)
                {
                    continue;
                }

                if (onlyName != null && !string.Equals(source.Name, onlyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(new ResponseCrossReferenceSource
                {
                    Name = source.Name,
                    Path = source.Path,
                    Address = source.Address,
                    Device = source.Device,
                    TypeName = source.TypeName,
                    References = BuildCrossReferenceReferenceList(source.References),
                    Children = maxDepth > 0 ? BuildCrossReferenceSourceList(source.Children, null, maxDepth - 1) : null
                });
            }

            return list;
        }

        /// <summary>
        /// A block/type's References mix "who else uses me" (ReferenceType.UsedBy - what callers
        /// actually want) with "what do I use internally" (ReferenceType.Uses - every tag/block
        /// touched by its own logic, which is usually large and not relevant to that question).
        /// Drop Uses-only references entirely, and their Uses-only locations otherwise.
        /// </summary>
        private static List<ResponseCrossReferenceReference> BuildCrossReferenceReferenceList(ReferenceObjectComposition? references)
        {
            var list = new List<ResponseCrossReferenceReference>();

            if (references == null)
            {
                return list;
            }

            foreach (var reference in references)
            {
                if (reference == null)
                {
                    continue;
                }

                var locations = BuildCrossReferenceLocationList(reference.Locations)
                    .Where(l => l.ReferenceType != "Uses")
                    .ToList();

                if (locations.Count == 0)
                {
                    continue;
                }

                list.Add(new ResponseCrossReferenceReference
                {
                    Name = reference.Name,
                    Path = reference.Path,
                    Address = reference.Address,
                    Device = reference.Device,
                    TypeName = reference.TypeName,
                    Locations = locations
                });
            }

            return list;
        }

        private static List<ResponseCrossReferenceLocation> BuildCrossReferenceLocationList(LocationComposition? locations)
        {
            var list = new List<ResponseCrossReferenceLocation>();

            if (locations == null)
            {
                return list;
            }

            foreach (var location in locations)
            {
                if (location == null)
                {
                    continue;
                }

                list.Add(new ResponseCrossReferenceLocation
                {
                    Name = location.Name,
                    Address = location.Address,
                    TypeName = location.TypeName,
                    Access = location.Access.ToString(),
                    ReferenceType = location.ReferenceType.ToString(),
                    ReferenceLocation = location.ReferenceLocation,
                    ReferencedAsName = location.ReferencedAsName
                });
            }

            return list;
        }

        public static string? MultilingualTextToString(MultilingualText? text)
        {
            if (text == null)
            {
                return null;
            }

            foreach (var item in text.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                {
                    return item.Text;
                }
            }

            return null;
        }
    }
}
