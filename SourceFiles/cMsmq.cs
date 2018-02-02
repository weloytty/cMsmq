using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace cMsmq
{
    #region Enumerations

    [Flags]
    public enum MessageQueueAccessRights
    {
        DeleteMessage = 0x00000001,
        PeekMessage = 0x00000002,
        ReceiveMessage = (DeleteMessage | PeekMessage),
        WriteMessage = 0x00000004,
        DeleteJournalMessage = 0x00000008,
        ReceiveJournalMessage = (DeleteJournalMessage | PeekMessage),
        SetQueueProperties = 0x00000010,
        GetQueueProperties = 0x00000020,
        DeleteQueue = 0x00010000,
        GetQueuePermissions = 0x00020000,
        GenericWrite = (GetQueueProperties | GetQueuePermissions | WriteMessage),
        GenericRead = (GetQueueProperties | GetQueuePermissions | ReceiveMessage | ReceiveJournalMessage),
        ChangeQueuePermissions = 0x00040000,
        TakeQueueOwnership = 0x00080000,
        FullControl = (ReceiveMessage |
                       ReceiveJournalMessage |
                       WriteMessage |
                       SetQueueProperties |
                       GetQueueProperties |
                       DeleteQueue |
                       GetQueuePermissions |
                       ChangeQueuePermissions |
                       TakeQueueOwnership)
    };

    [Flags]
    internal enum SecurityInformation : uint
    {
        Owner = 0x00000001,
        Group = 0x00000002,
        Dacl = 0x00000004,
        Sacl = 0x00000008,
    }

    internal enum ACL_INFORMATION_CLASS
    {
        AclRevisionInformation = 1,
        AclSizeInformation
    }

    #endregion

    #region Structures

    [StructLayoutAttribute(LayoutKind.Sequential)]
    internal class SECURITY_DESCRIPTOR
    {
        public byte revision;
        public byte size;
        public short control;
        public IntPtr owner;
        public IntPtr group;
        public IntPtr sacl;
        public IntPtr dacl;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ACL_SIZE_INFORMATION
    {
        public uint AceCount;
        public uint AclBytesInUse;
        public uint AclBytesFree;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ACE_HEADER
    {
        public byte AceType;
        public byte AceFlags;
        public short AceSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ACCESS_ALLOWED_ACE
    {
        public ACE_HEADER Header;
        public MessageQueueAccessRights Mask;
        public int SidStart;
    }

    #endregion

    public class Security
    {
        private const int MQ_OK = 0x0;
        private const uint MQ_ERROR_ACCESS_DENIED = 0xC00E0025;
        private const uint MQ_ERROR_ILLEGAL_FORMATNAME = 0xC00E001E;
        private const uint MQ_ERROR_NO_DS = 0xC00E0013;
        private const uint MQ_ERROR_PRIVILEGE_NOT_HELD = 0xC00E0026;
        private const uint MQ_ERROR_SECURITY_DESCRIPTOR_TOO_SMALL = 0xC00E0023;
        private const uint MQ_ERROR_UNSUPPORTED_FORMATNAME_OPERATION = 0xC00E0020;
        private const uint MQ_ERROR_QUEUE_NOT_FOUND = 0xC00E0003;
        private const uint MQ_ERROR_FORMATNAME_BUFFER_TOO_SMALL = 0xC00E001F;
        private const uint MQ_ERROR_ILLEGAL_QUEUE_PATHNAME = 0xC00E0014;
        private const uint MQ_ERROR_SERVICE_NOT_AVAILABLE = 0xC00E000B;

        private static readonly Dictionary<uint, string> ErrorMessages = new Dictionary<uint, string>
        {
            {MQ_ERROR_ACCESS_DENIED, "Access is denied."},
            {MQ_ERROR_ILLEGAL_FORMATNAME, "The specified format name is not valid."},
            {MQ_ERROR_NO_DS, "A connection with the directory service cannot be established."},
            {MQ_ERROR_PRIVILEGE_NOT_HELD, "The process owner does not have the required privileges to perform the operation."},
            {MQ_ERROR_SECURITY_DESCRIPTOR_TOO_SMALL, "The buffer is too small to hold the security descriptor."},
            {MQ_ERROR_UNSUPPORTED_FORMATNAME_OPERATION, "The requested operation is not supported for the specified format name."},
            {MQ_ERROR_QUEUE_NOT_FOUND, "Message Queuing cannot find the queue."},
            {MQ_ERROR_FORMATNAME_BUFFER_TOO_SMALL, "MQ_ERROR_FORMATNAME_BUFFER_TOO_SMALL"},
            {MQ_ERROR_ILLEGAL_QUEUE_PATHNAME, "MQ_ERROR_ILLEGAL_QUEUE_PATHNAME"},
            {MQ_ERROR_SERVICE_NOT_AVAILABLE, "MQ_ERROR_SERVICE_NOT_AVAILABLE"}
        };

        private static string GetErrorMessage(uint errorCode)
        {
            return ErrorMessages[errorCode];
        }

        private static string TranslateUserNameToSid(string userName)
        {
            var account = new NTAccount(userName);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));

            return sid.ToString();
        }

        private static string TranslateSidToUserName(SecurityIdentifier sid)
        {
            var account = (NTAccount)sid.Translate(typeof(NTAccount));

            return account.ToString();
        }

        private static ACCESS_ALLOWED_ACE GetAce(IntPtr pSecurityDescriptor, string sid)
        {
            bool daclPresent;
            bool daclDefaulted;
            IntPtr pAcl = IntPtr.Zero;

            Security.GetSecurityDescriptorDacl(pSecurityDescriptor, out daclPresent, ref pAcl, out daclDefaulted);

            if (daclPresent)
            {
                ACL_SIZE_INFORMATION AclSize = new ACL_SIZE_INFORMATION();
                Security.GetAclInformation(pAcl, ref AclSize, (uint)Marshal.SizeOf(typeof(ACL_SIZE_INFORMATION)), ACL_INFORMATION_CLASS.AclSizeInformation);

                for (int i = 0; i < AclSize.AceCount; i++)
                {
                    IntPtr pAce;
                    Security.GetAce(pAcl, i, out pAce);
                    ACCESS_ALLOWED_ACE ace = (ACCESS_ALLOWED_ACE)Marshal.PtrToStructure(pAce, typeof(ACCESS_ALLOWED_ACE));

                    IntPtr iter = (IntPtr)((long)pAce + (long)Marshal.OffsetOf(typeof(ACCESS_ALLOWED_ACE), "SidStart"));
                    byte[] sidBytes = null;
                    int sidSize = (int)Security.GetLengthSid(iter);
                    sidBytes = new byte[sidSize];
                    Marshal.Copy(iter, sidBytes, 0, sidSize);
                    IntPtr pSid;
                    Security.ConvertSidToStringSid(sidBytes, out pSid);
                    string strSid = Marshal.PtrToStringAuto(pSid);

                    if (strSid == sid)
                    {
                        return ace;
                    }
                }

                throw new Exception(string.Format("No ACE for SID '{0}' found in Security Descriptor.", sid));
            }
            else
            {
                throw new Exception("No DACL found in Security Descriptor.");
            }
        }

        private static string PathNameToFormatName(string queuePath)
        {
            int formatNameLength = 54; // max: 44 for public, 54 for private
            StringBuilder formatNameBuffer = new StringBuilder(formatNameLength);
            uint result = Security.MQPathNameToFormatName(queuePath, formatNameBuffer, ref formatNameLength);
            if (result != Security.MQ_OK)
            {
                string message = Security.GetErrorMessage(result);
                throw new Exception(message);
            }
            return formatNameBuffer.ToString();
        }

        private static GCHandle GetSecurityDescriptorHandle(string queuePath, int securityInformation)
        {
            byte[] securityDescriptorBytes;
            int length;
            int lengthNeeded;
            uint result;

            string formatName = PathNameToFormatName(queuePath);

            result = Security.MQGetQueueSecurity(formatName, securityInformation, IntPtr.Zero, 0, out lengthNeeded);

            if (result != Security.MQ_ERROR_SECURITY_DESCRIPTOR_TOO_SMALL)
            {
                string message = Security.GetErrorMessage(result);
                throw new Exception(message);
            }

            length = lengthNeeded;
            securityDescriptorBytes = new byte[length];

            IntPtr pSecurityDescriptor = new IntPtr();
            GCHandle gchSecurityDescriptor = GCHandle.Alloc(securityDescriptorBytes, GCHandleType.Pinned);
            pSecurityDescriptor = gchSecurityDescriptor.AddrOfPinnedObject();

            result = Security.MQGetQueueSecurity(formatName, securityInformation, pSecurityDescriptor, length, out lengthNeeded);

            if (result != Security.MQ_OK)
            {
                gchSecurityDescriptor.Free();

                string message = Security.GetErrorMessage(result);
                throw new Exception(message);
            }

            var securityDescriptor = new SECURITY_DESCRIPTOR();
            Marshal.PtrToStructure(pSecurityDescriptor, securityDescriptor);

            return gchSecurityDescriptor;
        }

        public static MessageQueueAccessRights GetAccessMask(string queuePath, string userName)
        {
            var sid = TranslateUserNameToSid(userName);
            var gchSecurityDescriptor = GetSecurityDescriptorHandle(queuePath, (int)SecurityInformation.Dacl);
            var ace = GetAce(gchSecurityDescriptor.AddrOfPinnedObject(), sid);
            var aceMask = ace.Mask;

            gchSecurityDescriptor.Free();

            return aceMask;
        }

        public static string GetOwner(string queuePath)
        {
            IntPtr pOwner;
            bool ownerDefaulted;

            var gchSecurityDescriptor = GetSecurityDescriptorHandle(queuePath, (int)SecurityInformation.Owner);
            Security.GetSecurityDescriptorOwner(gchSecurityDescriptor.AddrOfPinnedObject(), out pOwner, out ownerDefaulted);

            var ownerSid = new SecurityIdentifier(pOwner);
            string ownerUserName = TranslateSidToUserName(ownerSid);

            gchSecurityDescriptor.Free();

            return ownerUserName;
        }

        #region P/Invoke Definitions

        [DllImport("mqrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint MQPathNameToFormatName(
            string lpwcsPathName,
            StringBuilder lpwcsFormatName,
            ref int lpdwCount
        );

        [DllImport("mqrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint MQGetQueueSecurity(
            [MarshalAs(UnmanagedType.LPWStr)] string lpwcsFormatName,
            int SecurityInformation,
            IntPtr pSecurityDescriptor,
            int nLength,
            out int lpnLengthNeeded
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorDacl(
            IntPtr pSecurityDescriptor,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclPresent,
            ref IntPtr pDacl,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclDefaulted
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorOwner(
            IntPtr pSecurityDescriptor,
            out IntPtr pOwner,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbOwnerDefaulted
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetAclInformation(
            IntPtr pAcl,
            ref ACL_SIZE_INFORMATION pAclInformation,
            uint nAclInformationLength,
            ACL_INFORMATION_CLASS dwAclInformationClass
         );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetAce(
            IntPtr pAcl,
            int dwAceIndex,
            out IntPtr pAce
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetLengthSid(
            IntPtr pSid
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertSidToStringSid(
            [MarshalAs(UnmanagedType.LPArray)] byte[] pSid,
            out IntPtr pStringSid
        );

        #endregion
    }
}
