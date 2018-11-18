from __future__ import print_function

import os, socket



def c(s):
    print(">",s)
    os.system(s)

def create_zips():
    os.chdir("t1")
    c("7z a ../t1.zip *")

    os.chdir("../t2")
    c("7z a ../t2.zip *")
    os.chdir("..")

def connect(addr, port):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.connect((addr, port))
    return s


def send_bulk_string(sock, text):
    sock.sendall("$" + str(len(text)) + "\r\n" + text)

def send(addr, port, command, text):
    s = connect(addr, port)
    s.send("+%s\r\n" % command)
    send_bulk_string(s, text)



PORT = 19802

SOCK_BUF_SIZE = 0x1000

def send_bytes_from(file, sock):
    while 1:
        data = file.read(SOCK_BUF_SIZE)
        if data == "":
            break
        sock.sendall(data)

def recv_bytes_to(file, sock, nbytes):
    remaining = nbytes
    while 1:
        data = sock.recv(SOCK_BUF_SIZE)
        file.write(data)
        remaining -= len(data)
        if remaining == 0:
            break

def sendfile(command, fname):
    cont = open(fname, "rb").read()
    return send("localhost", PORT, command, cont)

class EWinRMClient:
    def __init__(self, addr, port):
        self.addr = addr
        self.port = port
        self.sock = None

    def connect(self):
        self.sock = connect(self.addr, self.port)

    def send_raw(self,s):
        self.sock.sendall(s)

    def send_simple(self,s):
        """ oneliner simple string """
        self.sock.sendall(b"+" + s + b"\r\n")

    def send_bulk_string(self,s):
        """ string with length """
        self.sock.sendall("$" + str(len(s)) + "\r\n" + s)

    def send_bulk_string_from_file(self,fname):
        """ send whole file """
        size = os.path.getsize(fname)
        self.sock.sendall("$" + str(size) + "\r\n")
        fstream = open(fname, "rb")
        send_bytes_from(fstream, self.sock)

    def recv_line(self):
        chars = []
        while 1:
            b = self.sock.recv(1)
            print("Byte: '%s'" % b)
            if b == "\n":
                return b"".join(chars[:-1])
            chars.append(b)

    def recv_bulk_string_to_file(self, fobj):
        line = self.recv_line()
        print(line)
        assert line.startswith("$")
        count = int(line[1:])
        recv_bytes_to(fobj, self.sock, count)


    def run_text(self, cont):
        self.send_simple("run")
        self.send_bulk_string(cont)

    def run_file(self, fname):
        self.run_text(open(fname, "rb"))


    def run_zip_file(self, fname):
        self.send_simple(b"ziprun")
        self.send_bulk_string_from_file(fname)


    def get(self, fname, localname):
        self.send_simple("get " + fname)
        with open(localname, "wb") as f:
            self.recv_bulk_string_to_file(f)


c = EWinRMClient("localhost", PORT)
c.connect()

# c.run_zip_file("c:/dl/sqlops-windows-0.23.6.zip")
c.get("c:/dl/sqlops-windows-0.23.6.zip", "c:/t/test.zip")
#sendfile("run", "t4.py")
#sendfile("run", "t3.cmd")

#create_zips()
#sendfile("ziprun", "t1.zip")
#sendfile("ziprun", "t2.zip")


