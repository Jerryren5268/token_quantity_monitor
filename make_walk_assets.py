"""Extract Gemini's actual walking artwork; no procedural pose deformation."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
repo = root
source = Image.open(root / 'docs/images/dragon-girl-walk-gemini.png').convert('RGB')
cells = []
for index in range(8):
    col, row = index % 4, index // 4
    cell = source.crop((col*source.width//4, row*source.height//2,
                        (col+1)*source.width//4, (row+1)*source.height//2))
    rgb = np.asarray(cell).astype(float)
    green = rgb[:,:,1] - np.maximum(rgb[:,:,0], rgb[:,:,2])
    alpha = np.clip((70-green)/58, 0, 1)
    rgb[:,:,1] = np.minimum(rgb[:,:,1], np.maximum(rgb[:,:,0],rgb[:,:,2])+6)
    cutout = Image.fromarray(np.dstack((rgb.astype('uint8'), (alpha*255).astype('uint8'))))
    bounds = cutout.getchannel('A').point(lambda v:255 if v>=128 else 0).getbbox()
    # Align the head rather than the tail, whose silhouette moves during a step.
    head = np.asarray(cutout.getchannel('A'))[30:110] >= 128
    head_columns = np.nonzero(head)[1]
    anchor = (head_columns.min()+head_columns.max())/2
    cells.append((cutout, bounds, anchor))

scale = 216 / max(bounds[3]-bounds[1] for _,bounds,_ in cells)
frames = []
for index,(cutout,bounds,anchor) in enumerate(cells):
    sprite = Image.new('RGBA',(224,224))
    figure = cutout.crop(bounds)
    figure = figure.resize((round(figure.width*scale),round(figure.height*scale)),Image.Resampling.LANCZOS)
    x = round(130-(anchor-bounds[0])*scale)
    sprite.alpha_composite(figure,(x,220-figure.height))
    sprite.save(repo/f'pose-walk-{index:02}.png')
    frames.append(sprite)

sheet = Image.new('RGB',(896,448),'#e7eaf3')
for index,frame in enumerate(frames):
    sheet.paste(frame,((index%4)*224,(index//4)*224),frame)
(repo/'docs/images').mkdir(parents=True,exist_ok=True)
sheet.save(repo/'docs/images/dragon-girl-walking-frames.png')

preview = []
for tick in range(96):
    image = Image.new('RGB',(448,256),'#e7eaf3')
    ImageDraw.Draw(image).line((0,242,448,242),fill='#c0b5ce',width=2)
    left = tick>=48
    x = round(12+(47-(tick%48) if left else tick)*192/47)
    sprite = frames[tick%8]
    if left: sprite=sprite.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    image.paste(sprite,(x,20),sprite)
    preview.append(image)
preview[0].save(repo/'docs/images/dragon-girl-walking.gif',save_all=True,append_images=preview[1:],duration=100,loop=0)
print('Saved 8 transparent walking sprites, a sheet and a moving GIF.')
